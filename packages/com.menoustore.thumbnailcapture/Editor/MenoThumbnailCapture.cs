using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.PostProcessing;

namespace MenoTools.ThumbnailCapture
{
    // Post Processing Stack (PPv2) 対応のサムネイル撮影ツール。
    public class MenoThumbnailCapture : EditorWindow
    {
        private enum CaptureSource { Camera, SceneView }

        private enum ResolutionPreset
        {
            [InspectorName("HD (1280x720)")] HD,
            [InspectorName("FHD (1920x1080)")] FHD,
            [InspectorName("4K/UHD (3840x2160)")] UHD4K,
            [InspectorName("8K (7680x4320)")] UHD8K,
            [InspectorName("正方形 2048x2048")] Square2K,
            [InspectorName("正方形 1024x1024")] Square1K,
            [InspectorName("カスタム")] Custom
        }

        private static readonly Vector2Int[] PresetSizes =
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1920, 1080),
            new Vector2Int(3840, 2160),
            new Vector2Int(7680, 4320),
            new Vector2Int(2048, 2048),
            new Vector2Int(1024, 1024),
        };

        private enum Orientation { Horizontal, Vertical }
        private enum BackgroundType { CameraDefault, Skybox, SolidColor, Transparent }
        private enum SupersampleScale { x1 = 1, x2 = 2, x4 = 4 }

        private CaptureSource source = CaptureSource.Camera;

        private Camera targetCamera;
        private ResolutionPreset resolution = ResolutionPreset.FHD;
        private Orientation orientation = Orientation.Horizontal;
        private Vector2Int customSize = new Vector2Int(1920, 1080);
        private BackgroundType background = BackgroundType.CameraDefault;
        private Color solidColor = Color.white;

        private bool usePostProcessing = true;
        private int warmupFrames = 4;
        private SupersampleScale supersample = SupersampleScale.x2;

        private string outputFolder;
        private string fileName = "thumbnail";

        private int sceneViewLongEdge = 3840;

        [MenuItem("Meno Tools/Thumbnail Capture")]
        public static void ShowWindow()
        {
            var window = GetWindow<MenoThumbnailCapture>("Thumbnail Capture");
            window.minSize = new Vector2(340, 440);
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(outputFolder))
                outputFolder = Application.dataPath + "/MenoTools/ThumbnailCapture/Output";
        }

        private void OnGUI()
        {
            GUILayout.Space(6);
            EditorGUILayout.LabelField("Meno Thumbnail Capture", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("PPS (Post Processing Stack v2) 対応", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("撮影位置", EditorStyles.boldLabel);
            source = (CaptureSource)GUILayout.Toolbar((int)source, new[] { "カメラ位置", "シーン位置（現在の視点）" });

            if (source == CaptureSource.Camera)
            {
                targetCamera = (Camera)EditorGUILayout.ObjectField("撮影カメラ", targetCamera, typeof(Camera), true);
            }
            else
            {
                EditorGUILayout.HelpBox("Unity Editor が内部に持つ Scene ビュー専用カメラを使うため、カメラの配置は不要です。", MessageType.None);
            }
            EditorGUILayout.Space();

            Vector2Int size = new Vector2Int(1920, 1080);

            if (source == CaptureSource.Camera)
            {
                resolution = (ResolutionPreset)EditorGUILayout.EnumPopup("解像度", resolution);
                if (resolution == ResolutionPreset.Custom)
                {
                    customSize = EditorGUILayout.Vector2IntField(" ", customSize);
                    size = customSize;
                }
                else
                {
                    size = PresetSizes[(int)resolution];
                    if (size.x != size.y)
                    {
                        orientation = (Orientation)EditorGUILayout.EnumPopup(" ", orientation);
                        if (orientation == Orientation.Vertical)
                            size = new Vector2Int(size.y, size.x);
                    }
                }
            }
            else
            {
                var sv = SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("長辺サイズ", GUILayout.Width(EditorGUIUtility.labelWidth));
                    if (GUILayout.Button("HD")) sceneViewLongEdge = 1280;
                    if (GUILayout.Button("FHD")) sceneViewLongEdge = 1920;
                    if (GUILayout.Button("2K")) sceneViewLongEdge = 2048;
                    if (GUILayout.Button("4K")) sceneViewLongEdge = 3840;
                    if (GUILayout.Button("8K")) sceneViewLongEdge = 7680;
                    EditorGUILayout.EndHorizontal();
                    sceneViewLongEdge = EditorGUILayout.IntField("長辺(px)", sceneViewLongEdge);
                    sceneViewLongEdge = Mathf.Max(64, sceneViewLongEdge);

                    float aspect = sv.camera.aspect;
                    if (aspect <= 0f || float.IsNaN(aspect) || float.IsInfinity(aspect))
                        aspect = 16f / 9f;
                    size = aspect >= 1f
                        ? new Vector2Int(sceneViewLongEdge, Mathf.Max(2, Mathf.RoundToInt(sceneViewLongEdge / aspect)))
                        : new Vector2Int(Mathf.Max(2, Mathf.RoundToInt(sceneViewLongEdge * aspect)), sceneViewLongEdge);

                    EditorGUILayout.LabelField("出力サイズ", $"{size.x} x {size.y}（Sceneビューと同じアスペクト比を維持）");

                    long renderPixels = (long)size.x * size.y * (int)supersample * (int)supersample;
                    if (renderPixels > 4000L * 4000L)
                        EditorGUILayout.HelpBox("解像度が大きいため、レンダリングに時間がかかったりVRAM不足になる場合があります。スーパーサンプリングを下げることを推奨します。", MessageType.Warning);

                    if (usePostProcessing)
                    {
                        EditorGUILayout.Space();
                        bool sceneEffectsOn = sv.sceneViewState != null && sv.sceneViewState.showImageEffects;
                        bool hasScenePPSLayer = sv.camera.GetComponent<PostProcessLayer>() != null;
                        int volumeCount = FindObjectsOfType<PostProcessVolume>().Length;

                        if (!sceneEffectsOn)
                            EditorGUILayout.HelpBox(
                                "■重要: Scene ビュー右上の Effects メニューで「Post Processing」表示が OFF になっています。\nON にしないとエフェクトなしで撮影されます。",
                                MessageType.Warning);
                        else if (!hasScenePPSLayer)
                            EditorGUILayout.HelpBox(
                                "■重要: Scene ビューのカメラに PostProcessLayer が見つかりません。\nエフェクトなしで撮影される可能性が高いです。",
                                MessageType.Warning);
                        else if (volumeCount == 0)
                            EditorGUILayout.HelpBox(
                                "■重要: シーン内に PostProcessVolume が見つかりません。\nエフェクトなしで撮影される可能性が高いです。",
                                MessageType.Warning);

                        EditorGUILayout.HelpBox(
                            "※ Local Volume の重み（距離による効き具合）まではここで判定できません。最終的な見た目は撮影結果で確認してください。",
                            MessageType.None);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("アクティブな Scene ビューが見つかりません。Scene タブを開いてください。", MessageType.Warning);
                }
            }

            EditorGUILayout.Space();
            background = (BackgroundType)EditorGUILayout.EnumPopup("背景", background);
            if (background == BackgroundType.SolidColor)
                solidColor = EditorGUILayout.ColorField("色", solidColor);
            if (background == BackgroundType.Transparent)
                EditorGUILayout.HelpBox("透明背景 + PostProcessing はブルーム等の効果で縁がにじむ場合があります。", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Post Processing", EditorStyles.boldLabel);
            usePostProcessing = EditorGUILayout.ToggleLeft("PostProcessLayer を有効にして撮影する", usePostProcessing);
            if (usePostProcessing && source == CaptureSource.Camera && targetCamera != null && targetCamera.GetComponent<PostProcessLayer>() == null)
                EditorGUILayout.HelpBox("撮影カメラに PostProcessLayer が付いていません。", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!usePostProcessing))
            {
                warmupFrames = EditorGUILayout.IntSlider(
                    new GUIContent("ウォームアップ回数", "オートエクスポージャーなど時間経過で変化するエフェクトを収束させるため、本撮影前に数回レンダリングします。"),
                    warmupFrames, 0, 16);
            }

            supersample = (SupersampleScale)EditorGUILayout.EnumPopup(
                new GUIContent("スーパーサンプリング", "指定倍率で高解像度レンダリング後に縮小し、アンチエイリアスの質を上げます。"),
                supersample);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出力先", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField(outputFolder);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string picked = EditorUtility.OpenFolderPanel("出力フォルダを選択", outputFolder, "");
                if (!string.IsNullOrEmpty(picked))
                    outputFolder = picked;
            }
            EditorGUILayout.EndHorizontal();
            fileName = EditorGUILayout.TextField("ファイル名", fileName);

            EditorGUILayout.Space(10);
            bool canCapture = source == CaptureSource.Camera
                ? targetCamera != null
                : SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null;

            using (new EditorGUI.DisabledScope(!canCapture))
            {
                if (GUILayout.Button("撮影する", GUILayout.Height(30)))
                    Capture(size);
            }
        }

        private void Capture(Vector2Int size)
        {
            int scale = (int)supersample;
            int renderWidth = size.x * scale;
            int renderHeight = size.y * scale;

            int maxTex = SystemInfo.maxTextureSize;
            if (renderWidth > maxTex || renderHeight > maxTex)
            {
                float clamp = Mathf.Min((float)maxTex / renderWidth, (float)maxTex / renderHeight);
                renderWidth = Mathf.Max(2, Mathf.FloorToInt(renderWidth * clamp));
                renderHeight = Mathf.Max(2, Mathf.FloorToInt(renderHeight * clamp));
                Debug.LogWarning($"[MenoThumbnailCapture] レンダリング解像度が上限({maxTex}px)を超えたため {renderWidth}x{renderHeight} に縮小しました。");
            }

            bool transparent = background == BackgroundType.Transparent;
            RenderTextureFormat rtFormat = transparent ? RenderTextureFormat.ARGB32 : RenderTextureFormat.DefaultHDR;
            if (!SystemInfo.SupportsRenderTextureFormat(rtFormat))
            {
                Debug.LogWarning($"[MenoThumbnailCapture] {rtFormat} が非対応のため ARGB32 にフォールバックします。");
                rtFormat = RenderTextureFormat.ARGB32;
            }

            // Sceneビューモードでは複製せず、今まさに表示されているカメラを直接借りて撮影し、
            // 終わったら元の状態に戻す（PostProcessLayerの複製がうまくいかないケースがあったため）。
            GameObject captureGO = null;
            Camera captureCamera;
            bool useLiveSceneCamera = source == CaptureSource.SceneView;

            RenderTexture originalTargetTexture = null;
            CameraClearFlags originalClearFlags = CameraClearFlags.Skybox;
            Color originalBackgroundColor = Color.black;
            bool originalAllowMSAA = true;
            bool originalPPSEnabled = true;

            if (!useLiveSceneCamera)
            {
                captureGO = Instantiate(targetCamera.gameObject);
                captureGO.hideFlags = HideFlags.HideAndDontSave;
                captureCamera = captureGO.GetComponent<Camera>();
                captureCamera.enabled = false;
            }
            else
            {
                captureCamera = SceneView.lastActiveSceneView.camera;
                originalTargetTexture = captureCamera.targetTexture;
                originalClearFlags = captureCamera.clearFlags;
                originalBackgroundColor = captureCamera.backgroundColor;
                originalAllowMSAA = captureCamera.allowMSAA;
            }

            captureCamera.allowMSAA = false; // AAはスーパーサンプリングで担うため、カメラ側のMSAAとRTのミスマッチを避ける。

            var ppsLayer = captureCamera.GetComponent<PostProcessLayer>();
            if (ppsLayer != null)
            {
                originalPPSEnabled = ppsLayer.enabled;
                ppsLayer.enabled = usePostProcessing;
            }

            switch (background)
            {
                case BackgroundType.Skybox:
                    captureCamera.clearFlags = CameraClearFlags.Skybox;
                    break;
                case BackgroundType.SolidColor:
                    captureCamera.clearFlags = CameraClearFlags.SolidColor;
                    captureCamera.backgroundColor = new Color(solidColor.r, solidColor.g, solidColor.b, 1f);
                    break;
                case BackgroundType.Transparent:
                    captureCamera.clearFlags = CameraClearFlags.SolidColor;
                    captureCamera.backgroundColor = Color.clear;
                    break;
                case BackgroundType.CameraDefault:
                default:
                    break; // 元カメラ／Sceneビューの設定をそのまま使う。
            }

            RenderTexture highRes = new RenderTexture(renderWidth, renderHeight, 24, rtFormat, RenderTextureReadWrite.Default);
            highRes.antiAliasing = 1; // AAはスーパーサンプリングで担うため、MSAAとPPSの相性問題を避ける。
            if (!highRes.Create())
            {
                Debug.LogError($"[MenoThumbnailCapture] RenderTexture の作成に失敗しました ({renderWidth}x{renderHeight}, {rtFormat})。解像度かスーパーサンプリングを下げて試してください。");
                if (useLiveSceneCamera)
                {
                    captureCamera.clearFlags = originalClearFlags;
                    captureCamera.backgroundColor = originalBackgroundColor;
                    captureCamera.allowMSAA = originalAllowMSAA;
                    if (ppsLayer != null) ppsLayer.enabled = originalPPSEnabled;
                }
                else
                {
                    DestroyImmediate(captureGO);
                }
                return;
            }
            captureCamera.targetTexture = highRes;
            captureCamera.aspect = (float)renderWidth / renderHeight;
            RenderTexture.active = highRes;

            for (int i = 0; i < warmupFrames; i++)
                captureCamera.Render();
            captureCamera.Render();

            RenderTexture finalRT = RenderTexture.GetTemporary(size.x, size.y, 0, rtFormat, RenderTextureReadWrite.Default);
            finalRT.filterMode = FilterMode.Bilinear;
            Graphics.Blit(highRes, finalRT);

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = finalRT;

            TextureFormat texFormat = transparent ? TextureFormat.ARGB32 : TextureFormat.RGB24;
            Texture2D screenshot = new Texture2D(size.x, size.y, texFormat, false);
            screenshot.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            screenshot.Apply();

            RenderTexture.active = prevActive;
            highRes.Release();
            RenderTexture.ReleaseTemporary(finalRT);

            if (!useLiveSceneCamera)
            {
                DestroyImmediate(captureGO);
            }
            else
            {
                // Sceneビューのカメラを元の状態に戻す。
                captureCamera.targetTexture = originalTargetTexture;
                captureCamera.clearFlags = originalClearFlags;
                captureCamera.backgroundColor = originalBackgroundColor;
                captureCamera.allowMSAA = originalAllowMSAA;
                captureCamera.ResetAspect();
                if (ppsLayer != null)
                    ppsLayer.enabled = originalPPSEnabled;
                SceneView.RepaintAll();
            }

            Directory.CreateDirectory(outputFolder);
            string safeName = string.IsNullOrEmpty(fileName) ? "thumbnail" : fileName;
            string path = Path.Combine(outputFolder, $"{safeName}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
            File.WriteAllBytes(path, screenshot.EncodeToPNG());
            DestroyImmediate(screenshot);

            Debug.Log($"[MenoThumbnailCapture] 撮影完了: {path}");

            if (path.Replace("\\", "/").StartsWith(Application.dataPath))
            {
                AssetDatabase.Refresh();
                string assetPath = "Assets" + path.Replace("\\", "/").Substring(Application.dataPath.Length);
                var obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (obj != null)
                    EditorGUIUtility.PingObject(obj);
            }
        }
    }
}
