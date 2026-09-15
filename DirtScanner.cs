using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace KoboldVision
{
    internal sealed class DirtScanner : IDisposable
    {
        public const int CoarseResolution = Resolution >> CoarseShift;

        private const string EvaluationShader = "CleaningEvaluateBlit";
        private const int Resolution = 512;
        private const int CoarseShift = 1;
        private const float ScanInterval = 0.15f;

        private const int DirtThreshold = 10;
        private const int DirtFull = 100;
        private const int SoapThreshold = 6;
        private const int SoapFull = 64;

        private static readonly int CleaningMask = Shader.PropertyToID("_CleaningMask");
        private static readonly int RinsingMask = Shader.PropertyToID("_RinsingMask");
        private static readonly int DirtyMap = Shader.PropertyToID("_DirtyMap");

        private static readonly Color32 DirtColor = Linear(255, 120, 10);
        private static readonly Color32 SoapColor = Linear(60, 215, 255);

        private readonly Material _evaluation;
        private readonly RenderTexture _masks;

        private readonly Texture2D _upload;
        private readonly RenderTexture _spots;
        private readonly Color32[] _coarse = new Color32[CoarseResolution * CoarseResolution];
        private readonly Color32[] _coarseScratch = new Color32[CoarseResolution * CoarseResolution];
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly byte[] _dirtCurve = Curve(DirtThreshold, DirtFull);
        private readonly byte[] _soapCurve = Curve(SoapThreshold, SoapFull);

        private readonly Color32[] _dirtColors = Premultiplied(DirtColor);
        private readonly Color32[] _soapColors = Premultiplied(SoapColor);
        private float _nextScan;
        private bool _pending;
        private int _generation;
        private bool _disposed;

        private DirtScanner(Shader evaluation)
        {
            _evaluation = new Material(evaluation) { name = "Kobold Vision Evaluation" };
            _masks = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "Kobold Vision Masks" };
            _upload = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true) { name = "Kobold Vision Spots Upload" };
            _spots = new RenderTexture(new RenderTextureDescriptor(Resolution, Resolution, GraphicsFormat.R8G8B8A8_UNorm, 0) { useMipMap = true, autoGenerateMips = false })
            {
                name = "Kobold Vision Spots",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            Clear();
        }

        public Texture Spots { get { return _spots; } }

        // LowRes
        public Color32[] Coarse { get { return _coarse; } }

        public int Version { get; private set; }

        public bool HasResult { get; private set; }

        // Texels of the last scan
        public int DirtTexels { get; private set; }
        public int SoapTexels { get; private set; }

        // Null when the measuring shader isn't loaded
        public static DirtScanner Create()
        {
            var shader = Shader.Find(EvaluationShader);
            return shader != null && shader.isSupported ? new DirtScanner(shader) : null;
        }

        public void Restart()
        {
            HasResult = false;
            _nextScan = 0f;
        }

        public unsafe void Clear()
        {
            _generation++;
            _pending = false;
            HasResult = false;
            _nextScan = 0f;
            var pixels = _upload.GetRawTextureData<byte>();
            UnsafeUtility.MemClear(NativeArrayUnsafeUtility.GetUnsafePtr(pixels), pixels.Length);
            Upload();
            Array.Clear(_coarse, 0, _coarse.Length);
            Version++;
        }

        public void Scan(SkinnedMeshRenderer body, float now)
        {
            if (_pending || now < _nextScan) return;
            _nextScan = now + ScanInterval;

            body.GetPropertyBlock(_block);
            _evaluation.SetTexture(CleaningMask, OrBlack(_block.GetTexture(CleaningMask)));
            _evaluation.SetTexture(RinsingMask, OrBlack(_block.GetTexture(RinsingMask)));
            var material = body.sharedMaterial;
            _evaluation.SetTexture(DirtyMap, OrBlack(material != null && material.HasProperty(DirtyMap) ? material.GetTexture(DirtyMap) : null));

            Graphics.Blit(Texture2D.whiteTexture, _masks, _evaluation);

            _pending = true;
            int generation = _generation;
            AsyncGPUReadback.Request(_masks, 0, TextureFormat.RGBA32, request => OnReadback(request, generation));
        }

        public void Dispose()
        {
            _disposed = true;
            Object.Destroy(_evaluation);
            Object.Destroy(_masks);
            Object.Destroy(_upload);
            Object.Destroy(_spots);
        }

        private unsafe void OnReadback(AsyncGPUReadbackRequest request, int generation)
        {
            if (_disposed || generation != _generation) return;
            _pending = false;
            if (request.hasError) return;

            var masks = request.GetData<byte>();
            if (masks.Length < Resolution * Resolution * 4) return;
            byte* source = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(masks);
            var spots = (Color32*)NativeArrayUnsafeUtility.GetUnsafePtr(_upload.GetRawTextureData<byte>());
            int dirtTexels = 0;
            int soapTexels = 0;
            Array.Clear(_coarse, 0, _coarse.Length);
            fixed (byte* dirtCurve = _dirtCurve, soapCurve = _soapCurve)
            fixed (Color32* coarse = _coarse, dirtColors = _dirtColors, soapColors = _soapColors)
            {
                for (int y = 0; y < Resolution; y++)
                {
                    Color32* coarseRow = coarse + (y >> CoarseShift) * CoarseResolution;
                    for (int x = 0; x < Resolution; x++, source += 4, spots++)
                    {
                        int clean = source[0];
                        int dirt = source[2];
                        int dirtLeft = dirt > clean ? dirtCurve[dirt - clean] : 0;
                        int soapLeft = soapCurve[source[1]];
                        if (soapLeft == 0)
                        {
                            if (dirtLeft == 0)
                            {
                                *(uint*)spots = 0;
                                continue;
                            }
                            *spots = dirtColors[dirtLeft];
                            dirtTexels++;
                        }
                        else if (dirtLeft == 0)
                        {
                            *spots = soapColors[soapLeft];
                            soapTexels++;
                        }
                        else
                        {
                            dirtTexels++;
                            soapTexels++;
                            // Dirt priority over soap
                            if (dirtLeft >= soapLeft)
                            {
                                *spots = dirtColors[dirtLeft];
                            }
                            else
                            {
                                int dirtShare = dirtLeft * 255 / soapLeft;
                                Color32 d = dirtColors[soapLeft];
                                Color32 s = soapColors[soapLeft];
                                *spots = new Color32(
                                    (byte)((d.r * dirtShare + s.r * (255 - dirtShare)) / 255),
                                    (byte)((d.g * dirtShare + s.g * (255 - dirtShare)) / 255),
                                    (byte)((d.b * dirtShare + s.b * (255 - dirtShare)) / 255),
                                    (byte)soapLeft);
                            }
                        }
                        Color32* block = coarseRow + (x >> CoarseShift);
                        if (spots->a > block->a) *block = *spots;
                    }
                }
            }
            Upload();
            SoftenCoarse();
            DirtTexels = dirtTexels;
            SoapTexels = soapTexels;
            Version++;
            HasResult = true;
        }

        private void Upload()
        {
            _upload.Apply(false);
            if (!_spots.IsCreated()) _spots.Create();
            if (SystemInfo.copyTextureSupport != CopyTextureSupport.None) Graphics.CopyTexture(_upload, 0, 0, _spots, 0, 0);
            else Graphics.Blit(_upload, _spots);
            _spots.GenerateMips();
        }

        private unsafe void SoftenCoarse()
        {
            const int size = CoarseResolution;
            const int stride = size * 4;
            fixed (Color32* coarse = _coarse, scratch = _coarseScratch)
            {
                byte* map = (byte*)coarse;
                byte* across = (byte*)scratch;
                for (int y = 0; y < size; y++)
                {
                    byte* row = map + y * stride;
                    for (int x = 0; x < stride; x += 4)
                    {
                        Soften(x > 0 ? row + x - 4 : row + x, row + x, x < stride - 4 ? row + x + 4 : row + x, across + y * stride + x);
                    }
                }
                for (int y = 0; y < size; y++)
                {
                    byte* row = across + y * stride;
                    byte* below = y > 0 ? row - stride : row;
                    byte* above = y < size - 1 ? row + stride : row;
                    for (int x = 0; x < stride; x += 4) Soften(below + x, row + x, above + x, map + y * stride + x);
                }
            }
        }

        private static unsafe void Soften(byte* before, byte* center, byte* after, byte* result)
        {
            if ((*(uint*)before | *(uint*)center | *(uint*)after) == 0)
            {
                *(uint*)result = 0;
                return;
            }
            for (int i = 0; i < 4; i++)
            {
                int soft = (before[i] + 2 * center[i] + after[i]) >> 2;
                result[i] = (byte)(soft > center[i] ? soft : center[i]);
            }
        }

        private static Texture OrBlack(Texture texture)
        {
            return texture != null ? texture : Texture2D.blackTexture;
        }

        private static byte[] Curve(int threshold, int full)
        {
            var curve = new byte[256];
            for (int i = 0; i < curve.Length; i++)
            {
                float t = Mathf.Clamp01((i - threshold) / (float)(full - threshold));
                curve[i] = (byte)Mathf.RoundToInt(t * t * (3f - 2f * t) * 255f);
            }
            return curve;
        }

        private static Color32[] Premultiplied(Color32 color)
        {
            var colors = new Color32[256];
            for (int a = 0; a < colors.Length; a++) colors[a] = new Color32((byte)(color.r * a / 255), (byte)(color.g * a / 255), (byte)(color.b * a / 255), (byte)a);
            return colors;
        }

        private static Color32 Linear(byte r, byte g, byte b)
        {
            return new Color32(ToLinear(r), ToLinear(g), ToLinear(b), 255);
        }

        private static byte ToLinear(byte value)
        {
            return (byte)Mathf.RoundToInt(Mathf.GammaToLinearSpace(value / 255f) * 255f);
        }
    }
}
