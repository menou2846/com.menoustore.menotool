using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using UnityEditor.Animations;

namespace Menou.ParticleLiveManager
{
    public sealed class MenoParticleLiveManagerWindow : EditorWindow
    {
        [SerializeField] MenoParticleLiveProfile profile;
        [SerializeField] GameObject root;
        [SerializeField] PlayableDirector director;
        [SerializeField] GameObject avatar;
        [SerializeField] string liveName = "New Live";
        [SerializeField] AudioClip audio;
        [SerializeField] string draftPrefix = "MenoLive";
        [SerializeField] bool advanced;
        internal const string PackageId = "com.menoustore.particlelivemanager";
        internal const string PackageTemplatePath = "Packages/" + PackageId + "/Runtime/Templates/ParticleLiveMA.prefab";
        internal const string LegacyTemplatePath = "Assets/menou-store_めのうすとあ/gimmick/particleライブ_ワールド固定/パーティクルライブ MA.prefab";
        internal static string TemplatePath => AssetDatabase.LoadAssetAtPath<GameObject>(PackageTemplatePath) ? PackageTemplatePath : LegacyTemplatePath;
        string report;
        int recordingRequest;
        Vector2 scroll;
        [MenuItem("Meno Tools/Particle Live Manager V1")]
        public static void Open() => GetWindow<MenoParticleLiveManagerWindow>("Particle Live Manager V1");
        void OnEnable()
        {
            minSize = new Vector2(420, 400);
            if (root && !avatar)
            {
                var descriptor = root.GetComponentInParent<VRCAvatarDescriptor>();
                if (descriptor) avatar = descriptor.gameObject;
                if (root == avatar) root = null;
            }
            if (avatar) SelectAvatar(avatar);
        }
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("パーティクルライブ制作", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("アバターと音源を選んで「制作を始める」を押してください。必要な設定は自動で用意します。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
            {
                EditorGUI.BeginChangeCheck();
                var chosen = (GameObject)EditorGUILayout.ObjectField("アバター", avatar, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) Run(() => SelectAvatar(chosen));
                if (avatar && avatar.GetComponent<VRCAvatarDescriptor>() && !EditorUtility.IsPersistent(avatar))
                {
                    var lives = FindLives(avatar);
                    if (lives.Length > 1)
                    {
                        var index = Array.IndexOf(lives, root);
                        var names = new[] { "ライブを選んでください" }.Concat(lives.Select(g => AnimationUtility.CalculateTransformPath(g.transform, avatar.transform))).ToArray();
                        var selected = EditorGUILayout.Popup("編集するライブ", index + 1, names) - 1;
                        if (selected != index) { root = selected >= 0 ? lives[selected] : null; director = profile && Resolve<GameObject>(profile.rootId) == root ? Resolve<PlayableDirector>(profile.directorId) : null; }
                    }
                    EditorGUILayout.HelpBox(root ? "編集対象: " + root.name : lives.Length == 0 ? "ライブはまだ入っていません。制作開始時に追加します。" : "ライブが複数あります。編集するものを選んでください。", MessageType.None);
                }
                if (profile)
                {
                    var so = new SerializedObject(profile);
                    so.Update();
                    EditorGUILayout.PropertyField(so.FindProperty("liveName"), new GUIContent("ライブ表示名"));
                    EditorGUILayout.PropertyField(so.FindProperty("audio"), new GUIContent("音源"));
                    so.ApplyModifiedProperties();
                }
                else
                {
                    liveName = EditorGUILayout.TextField("ライブ表示名", liveName);
                    audio = (AudioClip)EditorGUILayout.ObjectField("音源", audio, typeof(AudioClip), false);
                }
                using (new EditorGUI.DisabledScope(!avatar))
                {
                    if (GUILayout.Button(director ? "Timelineを開く・音源を反映" : "制作を始める", GUILayout.Height(32))) Run(() =>
                    {
                        Prepare();
                        OpenRecording("particle");
                    });
                }
                if (director)
                {
                    EditorGUILayout.BeginHorizontal();
                    foreach (var category in new[] { "particle", "skybox", "FX" })
                        if (GUILayout.Button(category + " を編集")) Run(() => OpenRecording(category));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.HelpBox("particle・skybox・FXには録画用アニメーションを用意しています。各項目の赤い●で録画できます。実行音源のON/OFFは「ライブ」が担当します。", MessageType.Info);
                    if (GUILayout.Button("アバター用データを作成・更新", GUILayout.Height(28))) Run(() =>
                    {
                        EndOwnPreview();
                        ValidateDirector();
                        if (profile.audio) ApplyAudio();
                        report = MenoParticleLiveGenerator.Generate(root, director, profile);
                    });
                }
                advanced = EditorGUILayout.Foldout(advanced, "詳細設定・既存Timelineを使う", true);
                if (advanced)
                {
                    EditorGUI.BeginChangeCheck();
                    profile = (MenoParticleLiveProfile)EditorGUILayout.ObjectField("制作設定", profile, typeof(MenoParticleLiveProfile), false);
                    if (EditorGUI.EndChangeCheck() && profile)
                    {
                        root = Resolve<GameObject>(profile.rootId);
                        director = Resolve<PlayableDirector>(profile.directorId);
                        var descriptor = root ? root.GetComponentInParent<VRCAvatarDescriptor>() : null;
                        if (descriptor) avatar = descriptor.gameObject;
                    }
                    if (GUILayout.Button("制作設定を別に作成…")) Run(NewProfile);
                    if (profile)
                    {
                        var so = new SerializedObject(profile); so.Update();
                        EditorGUILayout.PropertyField(so.FindProperty("prefix"), new GUIContent("生成名の接頭辞")); so.ApplyModifiedProperties();
                        director = (PlayableDirector)EditorGUILayout.ObjectField("既存の制作用Timeline", director, typeof(PlayableDirector), true);
                        if (GUILayout.Button("このTimelineを使う")) Run(() => { ResolveLive(); Link(); });
                        if (GUILayout.Button("設定を確認")) Run(() => { ResolveLive(); report = MenoParticleLiveGenerator.Audit(root); });
                        if (GUILayout.Button("アバター用データの作成前チェック")) Run(() => { ValidateDirector(); report = MenoParticleLiveGenerator.Check(root, director, profile); });
                    }
                }
            }
            EditorGUILayout.HelpBox("Sceneの保存は手動です。Control Trackは手動プレビュー専用で、アバター用には変換しません。", MessageType.None);
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        internal static GameObject[] FindLives(GameObject target) => target
            ? target.GetComponentsInChildren<ModularAvatarMergeAnimator>(true)
                .Where(m => m.GetComponentInParent<VRCAvatarDescriptor>() && m.GetComponentInParent<VRCAvatarDescriptor>().gameObject == target && m.transform.Find("ライブ") && m.transform.Find("MA設定"))
                .Select(m => m.gameObject).Distinct().ToArray()
            : Array.Empty<GameObject>();
        internal void SelectAvatar(GameObject target)
        {
            if (target && (EditorUtility.IsPersistent(target) || !target.scene.IsValid() || !target.GetComponent<VRCAvatarDescriptor>()))
                throw new InvalidOperationException("Hierarchy上のアバター（VRC Avatar Descriptorが付いている一番上のオブジェクト）を指定してください。");
            avatar = target;
            var lives = FindLives(avatar);
            var linked = profile ? Resolve<GameObject>(profile.rootId) : null;
            if (linked && lives.Contains(linked)) root = linked;
            else if (!lives.Contains(root)) root = lives.Length == 1 ? lives[0] : null;
            if (avatar && lives.Length == 0 && profile && !string.IsNullOrEmpty(profile.rootId)) DetachPreviousLive();
            director = profile && root && linked == root ? Resolve<PlayableDirector>(profile.directorId) : null;
            report = null;
        }
        void DetachPreviousLive()
        {
            liveName = profile.liveName;
            audio = profile.audio;
            draftPrefix = profile.prefix;
            profile = null;
            director = null;
        }
        void ValidateAvatar()
        {
            if (!avatar || EditorUtility.IsPersistent(avatar) || !avatar.scene.IsValid() || !avatar.GetComponent<VRCAvatarDescriptor>() || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Hierarchy上のアバターを指定してください。");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("制作プレビューを止めてから実行してください。");
            if (director && director.state == PlayState.Playing) throw new InvalidOperationException("Timelineの▶を止めてから実行してください。");
        }
        void ResolveLive()
        {
            ValidateAvatar();
            var lives = FindLives(avatar);
            if (!lives.Contains(root)) root = lives.Length == 1 ? lives[0] : null;
            if (!root) throw new InvalidOperationException(lives.Length > 1 ? "編集するライブを選んでください。" : "先に「制作を始める」を押してください。");
        }
        void Prepare()
        {
            EndOwnPreview();
            ValidateAvatar();
            var lives = FindLives(avatar);
            if (!lives.Contains(root)) root = lives.Length == 1 ? lives[0] : null;
            if (lives.Length > 1 && !root) throw new InvalidOperationException("編集するライブを選んでください。");
            if (lives.Length == 0 && profile && !string.IsNullOrEmpty(profile.rootId)) DetachPreviousLive();
            if (profile && !string.IsNullOrEmpty(profile.rootId) && (!root || Resolve<GameObject>(profile.rootId) != root))
                throw new InvalidOperationException("この制作設定は別のライブ用です。詳細設定で制作設定を切り替えるか、別に作成してください。");
            var template = !root ? AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) : null;
            if (!root && !template) throw new InvalidOperationException("ライブの雛形が見つかりません: " + TemplatePath);
            if (root) MenoParticleLiveGenerator.ValidateRoot(root);
            if (!profile)
            {
                const string parent = "Assets/MenoTools/ParticleLiveManagerV1";
                var folder = "Assets";
                foreach (var part in new[] { "MenoTools", "ParticleLiveManagerV1", "Generated" })
                {
                    var next = folder + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(folder, part)))
                        throw new InvalidOperationException("制作データの保存先を作成できません: " + next);
                    folder = next;
                }
                profile = CreateInstance<MenoParticleLiveProfile>(); profile.liveName = liveName; profile.audio = audio; profile.prefix = draftPrefix;
                director = null;
                AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath(parent + "/Generated/LiveProfile.asset"));
            }
            if (!root)
            {
                var name = GameObjectUtility.GetUniqueNameForSibling(avatar.transform, template.name);
                root = (GameObject)PrefabUtility.InstantiatePrefab(template, avatar.transform);
                Undo.RegisterCreatedObjectUndo(root, "Add particle live to avatar");
                root.name = name;
            }
            Setup();
            if (profile.audio) ApplyAudio();
            report = "制作準備ができました。Timelineで音楽・演出を編集できます。Sceneは未保存です。";
        }
        void EndOwnPreview()
        {
            recordingRequest++;
            var window = TimelineEditor.GetWindow();
            if (!window || !director || TimelineEditor.inspectedDirector != director) return;
            window.playbackControls.Pause();
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var state = window.GetType().GetProperty("state", flags)?.GetValue(window, null);
            var preview = state?.GetType().GetProperty("previewMode", flags);
            if (preview == null) throw new NotSupportedException("このTimeline版のプレビュー停止には対応していません。");
            preview.SetValue(state, false, null);
            window.Repaint();
        }
        void OpenRecording(string category)
        {
            if (!director || !profile || profile.timeline != director.playableAsset) throw new InvalidOperationException("先に制作を始めてください。");
            var target = root.transform.Find("ライブ/" + category);
            if (!target) throw new InvalidOperationException(category + "が見つかりません。制作設定を更新してください。");
            var track = profile.timeline.GetOutputTracks().OfType<AnimationTrack>().SingleOrDefault(t => t.name == category && t.infiniteClip && director.GetGenericBinding(t) == target.GetComponent<Animator>());
            if (!track) throw new InvalidOperationException(category + "のRecordedが見つかりません。");
            var timelineWindow = TimelineEditor.GetOrCreateWindow();
            if (TimelineEditor.inspectedDirector != director) timelineWindow.SetTimeline(director);
            // Pin the director while selecting content; the Recorded clip remains editable.
            timelineWindow.locked = true;
            timelineWindow.Show();
            Selection.activeGameObject = target.gameObject;
            var request = ++recordingRequest;
            var expectedDirector = director;
            // Attach once after selection refresh; discard superseded button presses.
            EditorApplication.delayCall += () =>
            {
                if (this && request == recordingRequest && director == expectedDirector && target && track && Selection.activeGameObject == target.gameObject && TimelineEditor.inspectedDirector == expectedDirector)
                    Run(() => LinkRecordingWindow(track, target.gameObject));
            };
        }
        static void LinkRecordingWindow(AnimationTrack track, GameObject target)
        {
            // shortcut: Unity Timeline 1.7 editor API is internal; revalidate this bridge when upgrading Timeline.
            var assembly = typeof(TimelineEditor).Assembly;
            var action = assembly.GetType("UnityEditor.Timeline.EditTrackInAnimationWindow");
            var utilities = assembly.GetType("UnityEditor.Timeline.TimelineAnimationUtilities");
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var createData = action?.GetMethod("CreateTimeControlClipData", flags);
            var edit = utilities?.GetMethod("EditAnimationClipWithTimeController", flags);
            var animation = GetWindow<AnimationWindow>();
            var windowFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var unlink = typeof(AnimationWindow).GetMethod("UnlinkSequencer", windowFlags);
            var clearSelection = typeof(AnimationWindow).GetMethod("EditAnimationClip", windowFlags);
            if (createData == null || edit == null || unlink == null || clearSelection == null) throw new NotSupportedException("このTimeline版のAnimation編集連携には対応していません。");
            var timeline = TimelineEditor.GetWindow();
            var state = timeline.GetType().GetProperty("state", windowFlags).GetValue(timeline, null);
            var preview = state.GetType().GetProperty("previewMode", windowFlags);
            var wasPreviewing = (bool)preview.GetValue(state, null);
            unlink.Invoke(animation, null);
            // Unity otherwise skips replacing the time controller when the clip/root hash is unchanged.
            clearSelection.Invoke(animation, new object[] { null });
            var data = createData.Invoke(null, new object[] { track });
            var createControl = utilities.GetMethods(flags).Single(m => m.Name == "CreateTimeController" && m.GetParameters()[0].ParameterType == data.GetType());
            var control = createControl.Invoke(null, new[] { data });
            edit.Invoke(null, new object[] { track.infiniteClip, control, target });
            if (wasPreviewing) preview.SetValue(state, true, null);
            animation.Focus();
        }
        void Run(Action action)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Particle Live Manager V1");
            try { action(); }
            catch (Exception e) { report = e.Message; Debug.LogWarning("Particle Live Manager V1: " + e.Message); }
            finally { Undo.CollapseUndoOperations(group); }
        }
        void NewProfile()
        {
            var path = EditorUtility.SaveFilePanelInProject("制作設定", "ParticleLiveProfile", "asset", "新規の設定アセットを作ります。");
            if (string.IsNullOrEmpty(path)) return;
            profile = CreateInstance<MenoParticleLiveProfile>();
            director = null;
            AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssetIfDirty(profile);
        }
        internal static string Id(UnityEngine.Object o)
        {
            var id = GlobalObjectId.GetGlobalObjectIdSlow(o).ToString();
            if (GlobalObjectId.GetGlobalObjectIdSlow(o).identifierType == 0) id = "session:" + o.GetInstanceID();
            SessionState.SetInt("MenoParticleLive." + id, o.GetInstanceID());
            return id;
        }
        internal static T Resolve<T>(string id) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("session:", StringComparison.Ordinal))
                return EditorUtility.InstanceIDToObject(SessionState.GetInt("MenoParticleLive." + id, 0)) as T;
            if (!GlobalObjectId.TryParse(id, out var value)) return null;
            var result = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(value) as T;
            if (result) return result;
            result = EditorUtility.InstanceIDToObject(SessionState.GetInt("MenoParticleLive." + id, 0)) as T;
            return result && GlobalObjectId.GetGlobalObjectIdSlow(result).ToString() == id ? result : null;
        }
        void ValidateProfileRoot()
        {
            MenoParticleLiveGenerator.ValidateRoot(root);
            if (!string.IsNullOrEmpty(profile.rootId) && Resolve<GameObject>(profile.rootId) != root)
                throw new InvalidOperationException("この制作設定は別のルートに関連付いています。別の制作設定を作成してください。");
        }
        internal static GameObject Child(Transform parent, string name)
        {
            var matches = parent.Cast<Transform>().Where(t => t.name == name).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("同名の子が複数あります: " + name);
            if (matches.Length == 1) return matches[0].gameObject;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Particle Live setup");
            Undo.SetTransformParent(go.transform, parent, "Particle Live setup");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }
        void NormalizeParticleName()
        {
            var live = root.transform.Find("ライブ");
            var old = live.Find("ぱーちくる");
            if (!old) return;
            if (live.Find("particle")) throw new InvalidOperationException("particleと旧名ぱーちくるが両方あります。どちらも変更していません。");
            var controllers = root.transform.root.GetComponentsInChildren<Animator>(true).Where(a => a.runtimeAnimatorController).Select(a => a.runtimeAnimatorController)
                .Concat(root.transform.root.GetComponentsInChildren<ModularAvatarMergeAnimator>(true).Where(m => m.animator).Select(m => m.animator));
            var descriptor = root.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor) controllers = controllers.Concat((descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()).Concat(descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()).Where(l => l.animatorController).Select(l => l.animatorController));
            var clips = controllers.SelectMany(c => c.animationClips);
            if (profile.timeline)
                clips = clips.Concat(profile.timeline.GetOutputTracks().OfType<AnimationTrack>().SelectMany(t => t.GetClips().Select(c => c.asset as AnimationPlayableAsset).Where(a => a && a.clip).Select(a => a.clip).Concat(t.infiniteClip ? new[] { t.infiniteClip } : Array.Empty<AnimationClip>())));
            if (clips.Distinct().Any(c => AnimationUtility.GetCurveBindings(c).Concat(AnimationUtility.GetObjectReferenceCurveBindings(c)).Any(b => b.path.Split('/').Contains("ぱーちくる"))))
                throw new InvalidOperationException("旧名ぱーちくるを参照するアニメーションがあるため、名前変更を止めました。既存Clipは書き換えていません。");
            Undo.RecordObject(old.gameObject, "Rename particle container");
            old.name = "particle";
            PrefabUtility.RecordPrefabInstancePropertyModifications(old.gameObject);
        }
        void EnsureRecordingClips()
        {
            var names = new[] { "particle", "skybox", "FX" };
            var legacy = new[] { "パーティクル", "背景", "FX" };
            for (int i = 0; i < names.Length; i++)
            {
                var target = root.transform.Find("ライブ/" + names[i]);
                foreach (var track in profile.timeline.GetOutputTracks().OfType<AnimationTrack>().Where(t => t.name == names[i] || t.name == legacy[i]))
                {
                    var animator = director.GetGenericBinding(track) as Animator;
                    if (!animator || animator.transform != target) continue;
                    Undo.RecordObject(track, "Prepare recording animation");
                    track.name = names[i];
                    if (!track.infiniteClip && !track.GetClips().Any()) track.CreateInfiniteClip(profile.prefix + "_" + names[i] + "_Recorded");
                    EditorUtility.SetDirty(track);
                }
            }
            EditorUtility.SetDirty(profile.timeline);
            AssetDatabase.SaveAssetIfDirty(profile.timeline);
        }
        void EnsureRecordingControllers()
        {
            var names = new[] { "particle", "skybox", "FX" };
            if (profile.authoringControllers == null) profile.authoringControllers = new AnimatorController[3];
            if (profile.authoringControllers.Length != 3) throw new InvalidOperationException("制作Controllerの所有情報が変更されています。");
            for (int i = 0; i < names.Length; i++)
            {
                var animator = root.transform.Find("ライブ/" + names[i]).GetComponent<Animator>();
                if (animator && animator.runtimeAnimatorController && animator.runtimeAnimatorController != profile.authoringControllers[i])
                    throw new InvalidOperationException(names[i] + "には既存Controllerがあります。上書きしません。");
            }
            for (int i = 0; i < names.Length; i++)
            {
                var animator = root.transform.Find("ライブ/" + names[i]).GetComponent<Animator>();
                var track = profile.timeline.GetOutputTracks().OfType<AnimationTrack>().SingleOrDefault(t => t.name == names[i] && t.infiniteClip && director.GetGenericBinding(t) == animator);
                if (!animator || !track) continue;
                var controller = profile.authoringControllers[i];
                if (!controller)
                {
                    var path = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile.timeline)).Replace('\\', '/') + "/" + names[i] + "_Authoring.controller";
                    controller = AnimatorController.CreateAnimatorControllerAtPath(AssetDatabase.GenerateUniqueAssetPath(path));
                    controller.layers[0].stateMachine.AddState("Recorded").writeDefaultValues = false;
                    Undo.RecordObject(profile, "Track authoring controller ownership");
                    profile.authoringControllers[i] = controller;
                    AssetDatabase.SetLabels(controller, new[] { MenoParticleLiveAuthoringPlugin.Label, MenoParticleLiveAuthoringPlugin.OwnerPrefix + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile)) });
                }
                if (!MenoParticleLiveAuthoringPlugin.IsAuthoring(controller) || controller.layers.Length != 1 || controller.layers[0].stateMachine.states.Length != 1)
                    throw new InvalidOperationException(names[i] + "の制作Controllerが変更されています。");
                var state = controller.layers[0].stateMachine.states[0].state;
                Undo.RecordObject(state, "Link Recorded animation"); state.motion = track.infiniteClip;
                var sm = controller.layers[0].stateMachine;
                Undo.RecordObject(sm, "Set Recorded default state"); sm.defaultState = state;
                Undo.RecordObject(animator, "Assign authoring controller"); animator.runtimeAnimatorController = controller;
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                EditorUtility.SetDirty(state); EditorUtility.SetDirty(sm); EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
            }
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
        }
        void RemoveEmptyAudioAnimator()
        {
            var sound = root.transform.Find("ライブ/音源");
            var animator = sound ? sound.GetComponent<Animator>() : null;
            if (animator && !animator.runtimeAnimatorController && !animator.avatar && !profile.timeline.GetOutputTracks().Any(t => director.GetGenericBinding(t) == animator))
                Undo.DestroyObjectImmediate(animator);
        }
        void Setup()
        {
            ValidateProfileRoot();
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) throw new InvalidOperationException("Prefab Modeでは実行しません。");
            if (!director && !string.IsNullOrEmpty(profile.directorId)) throw new InvalidOperationException("制作用Timelineを見つけられません。詳細設定から指定してください。");
            if (director) ValidateDirector();
            bool newTimeline = !profile.timeline && !director;
            var live = root.transform.Find("ライブ");
            NormalizeParticleName();
            foreach (var name in new[] { "particle", "音源", "skybox", "FX" }) Child(live, name);
            if (!director)
            {
                if (!string.IsNullOrEmpty(profile.directorId)) throw new InvalidOperationException("関連付け済みDirectorを解決できません。対象Sceneを開き、Directorを指定してください。");
                if (!profile.timeline)
                {
                    var folder = MenoParticleLiveGenerator.OutputFolder(profile);
                    profile.timeline = CreateInstance<TimelineAsset>();
                    profile.timeline.name = profile.liveName;
                    AssetDatabase.CreateAsset(profile.timeline, folder + "/Authoring.playable");
                    AssetDatabase.SaveAssetIfDirty(profile.timeline);
                }
                var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(root.transform, profile.prefix + "_Authoring"));
                Undo.RegisterCreatedObjectUndo(go, "Particle Live authoring");
                Undo.SetTransformParent(go.transform, root.transform, "Particle Live authoring");
                go.tag = "EditorOnly";
                director = Undo.AddComponent<PlayableDirector>(go);
                director.playOnAwake = false;
                director.playableAsset = profile.timeline;
            }
            if (newTimeline)
            {
                var paths = new[] { "particle", "skybox", "FX" };
                var names = paths;
                for (int i = 0; i < paths.Length; i++)
                {
                    var target = live.Find(paths[i]).gameObject;
                    var animator = target.GetComponent<Animator>();
                    if (!animator) animator = Undo.AddComponent<Animator>(target);
                    var track = profile.timeline.CreateTrack<AnimationTrack>(null, names[i]);
                    Undo.RecordObject(director, "Bind performance track");
                    director.SetGenericBinding(track, animator);
                }
                EditorUtility.SetDirty(profile.timeline);
                AssetDatabase.SaveAssetIfDirty(profile.timeline);
            }
            Link();
            EnsureRecordingClips();
            EnsureRecordingControllers();
            RemoveEmptyAudioAnimator();
            report = "セットアップ完了。既存Track・MA・Constraintは変更していません。Sceneは未保存。音源反映は別操作です。";
        }
        void ValidateDirector()
        {
            ValidateProfileRoot();
            if (!director || EditorUtility.IsPersistent(director) || director.gameObject.scene != root.scene ||
                !director.CompareTag("EditorOnly") || !director.transform.IsChildOf(root.transform) ||
                director.transform == root.transform || director.transform.IsChildOf(root.transform.Find("ライブ")) ||
                director.transform.IsChildOf(root.transform.Find("MA設定")) || !(director.playableAsset is TimelineAsset))
                throw new InvalidOperationException("同じルート配下（ライブ・MA設定の外）のEditorOnly Director + Timelineが必要です。");
            if (director.state == PlayState.Playing || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Timeline/Animationのプレビューを停止してから実行してください。");
        }
        void Link()
        {
            ValidateDirector();
            Undo.RecordObject(profile, "Link authoring timeline");
            profile.rootId = Id(root); profile.directorId = Id(director);
            if (profile.timeline != director.playableAsset && profile.previewTrack)
                throw new InvalidOperationException("音声Trackのある制作設定のTimeline差し替えは行いません。別の制作設定を作成してください。");
            profile.timeline = (TimelineAsset)director.playableAsset;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            report = "関連付けました。既存TimelineのTrackは変更していません。";
        }
        void ApplyAudio()
        {
            ValidateDirector();
            if (!profile.audio) throw new InvalidOperationException("AudioClipを指定してください。");
            if (profile.timeline != director.playableAsset) throw new InvalidOperationException("先にTimelineを関連付けてください。");
            // Only a dedicated new preview track is owned; an imported Timeline remains read-only.
            if (!root.transform.Find("ライブ/音源")) throw new InvalidOperationException("先にHierarchyセットアップを実行してください。");
            var previewTrack = profile.previewTrack;
            if (previewTrack && (previewTrack.timelineAsset != profile.timeline || previewTrack.GetClips().Count() != 1 || !(previewTrack.GetClips().Single().asset is AudioPlayableAsset)))
                throw new InvalidOperationException("制作音声Trackが編集されています。自動変更を中止しました。");
            var source = Resolve<AudioSource>(profile.audioId);
            if (!source && !string.IsNullOrEmpty(profile.audioId)) throw new InvalidOperationException("実行音源の参照を解決できません。対象Sceneを確認してください。");
            var previewSource = Resolve<AudioSource>(profile.previewSourceId);
            if ((!previewSource && !string.IsNullOrEmpty(profile.previewSourceId)) || previewSource && (!previewSource.transform.IsChildOf(director.transform) || !previewSource.transform.parent.CompareTag("EditorOnly")))
                throw new InvalidOperationException("制作音源の参照が変更されています。");
            if (source && (!source.transform.IsChildOf(root.transform.Find("ライブ/音源")) || source.GetComponents<Component>().Any(c => !(c is Transform) && !(c is AudioSource))))
                throw new InvalidOperationException("実行用音源の参照が変更されています。自動変更を中止しました。");
            if (!source)
            {
                var runtime = new GameObject(GameObjectUtility.GetUniqueNameForSibling(root.transform.Find("ライブ/音源"), profile.prefix + "_RuntimeAudio"));
                Undo.RegisterCreatedObjectUndo(runtime, "Create runtime audio");
                Undo.SetTransformParent(runtime.transform, root.transform.Find("ライブ/音源"), "Create runtime audio");
                runtime.transform.localPosition = Vector3.zero;
                source = Undo.AddComponent<AudioSource>(runtime);
                source.playOnAwake = true; source.loop = false;
                runtime.SetActive(true);
            }
            Undo.RecordObject(source.gameObject, "Enable runtime audio");
            source.gameObject.SetActive(true);
            Undo.RecordObject(profile, "Change audio references");
            Undo.RecordObject(source, "Change runtime audio"); source.clip = profile.audio;
            profile.audioId = Id(source);
            if (previewTrack == null)
            {
                Undo.RegisterCompleteObjectUndo(profile.timeline, "Create preview audio track");
                previewTrack = profile.timeline.CreateTrack<AudioTrack>(null, "Meno Preview " + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile)));
                previewTrack.CreateClip<AudioPlayableAsset>();
                profile.previewTrack = previewTrack;
            }
            var clips = previewTrack.GetClips().ToArray();
            if (clips.Length != 1 || !(clips[0].asset is AudioPlayableAsset)) throw new InvalidOperationException("制作音声Trackが編集されています。自動変更を中止しました。");
            if (!previewSource)
            {
                var preview = new GameObject(GameObjectUtility.GetUniqueNameForSibling(director.transform, "Meno Preview Audio"));
                Undo.RegisterCreatedObjectUndo(preview, "Create preview audio");
                Undo.SetTransformParent(preview.transform, director.transform, "Create preview audio");
                previewSource = Undo.AddComponent<AudioSource>(preview);
                profile.previewSourceId = Id(previewSource);
            }
            Undo.RecordObject(previewSource, "Change preview audio"); previewSource.playOnAwake = false;
            Undo.RecordObject(clips[0].asset, "Change preview clip");
            ((AudioPlayableAsset)clips[0].asset).clip = profile.audio;
            Undo.RecordObject(previewTrack, "Change preview timing");
            clips[0].start = 0; clips[0].clipIn = 0; clips[0].timeScale = 1; clips[0].duration = profile.audio.length;
            Undo.RecordObject(director, "Bind preview audio"); director.SetGenericBinding(previewTrack, previewSource);
            EditorUtility.SetDirty(profile); EditorUtility.SetDirty(profile.timeline); EditorUtility.SetDirty(previewTrack); EditorUtility.SetDirty(clips[0].asset);
            AssetDatabase.SaveAssetIfDirty(profile.timeline); AssetDatabase.SaveAssetIfDirty(profile);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source.gameObject);
            EditorSceneManager.MarkSceneDirty(root.scene);
            report = "制作プレビュー音声と専用の実行用AudioSourceへ反映。生成FXは再生成が必要です。Sceneは未保存。";
        }
    }
}
