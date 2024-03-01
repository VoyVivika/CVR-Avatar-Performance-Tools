#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
#if CVR_CCK_EXISTS
using ABI.CCK.Components;
#endif

namespace Voy.AvatarHelpers {
    public class AvatarEvaluator : EditorWindow
    {
        public const string VERSION = "1.0.0";
        public const string VERSIONBASED = "By VoyVivika Based on Thry's Avatar Evaluator v1.3.6";

        [MenuItem("Voy/Avatar/Avatar Evaluator")]
        static void Init()
        {
            AvatarEvaluator window = (AvatarEvaluator)EditorWindow.GetWindow(typeof(AvatarEvaluator));
            window.titleContent = new GUIContent("Avatar Evaluation");
            window.Show();
        }

        [MenuItem("GameObject/Voy/Avatar/CVR Avatar Evaluator", true, 0)]
        static bool CanShowFromSelection() => Selection.activeGameObject != null;

        [MenuItem("GameObject/Voy/Avatar/CVR Avatar Evaluator", false, 0)]
        public static void ShowFromSelection()
        {
            AvatarEvaluator window = (AvatarEvaluator)EditorWindow.GetWindow(typeof(AvatarEvaluator));
            window.titleContent = new GUIContent("Avatar Calculator");
            window._avatar = Selection.activeGameObject;
            window.Show();
        }

        GUIContent refreshIcon;

        //ui variables
        GameObject _avatar;
        bool _writeDefaultsFoldout;
        bool _emptyStatesFoldout;
        Vector2 _scrollPosition;

        //eval variables
        long _vramSize = 0;

        int _grabpassCount = 0;
        bool _grabpassFoldout = false;

        (SkinnedMeshRenderer renderer, int verticies, int blendshapeCount)[] _skinendMeshesWithBlendshapes;
        long _totalBlendshapeVerticies = 0;
        bool _blendshapeFoldout;

        int _anyStateTransitions = 0;
        bool _anyStateFoldout = false;

        int _layerCount = 0;
        bool _layerCountFoldout = false;

        Shader[] _shadersWithGrabpass;

        //write defaults
        bool _writeDefault;
        string[] _writeDefaultoutliers;

        string[] _emptyStates;

        private void OnEnable() {
            refreshIcon = EditorGUIUtility.IconContent("RotateTool On", "Recalculate");
            if (_avatar != null) Evaluate();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"<size=20><color=magenta>CVR Avatar Evaluator</color></size> v{VERSION}", new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleCenter });
            EditorGUILayout.LabelField(VERSIONBASED, EditorStyles.centeredGreyMiniLabel);
            if (GUILayout.Button("Based on work by Thryrallo, Click here to follow them on twitter", EditorStyles.centeredGreyMiniLabel))
                Application.OpenURL("https://twitter.com/thryrallo");
            EditorGUILayout.Space();
            if (GUILayout.Button("Edited by VoyVivika for CVR CCK Compatibility, Click here to visit my Linktree!", EditorStyles.centeredGreyMiniLabel))
                Application.OpenURL("https://linktr.ee/voyvivika");

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _avatar != null;
                if(GUILayout.Button(refreshIcon, GUILayout.Width(30), GUILayout.Height(30))) {
                    Evaluate();
                }
                GUI.enabled = true;

                _avatar = (GameObject)EditorGUILayout.ObjectField(GUIContent.none, _avatar, typeof(GameObject), true, GUILayout.Height(30));
                if (EditorGUI.EndChangeCheck() && _avatar != null) {
                    Evaluate();
                }

            }

            if (_avatar == null)
            {
#if CVR_CCK_EXISTS
                IEnumerable<CVRAvatar> avatars = new List<CVRAvatar>();
                for(int i=0;i<EditorSceneManager.sceneCount;i++)

                    avatars = avatars.Concat( EditorSceneManager.GetSceneAt(i).GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CVRAvatar>()).Where( d => d.gameObject.activeInHierarchy) );
                if(avatars.Count() > 0)
                {
                    _avatar = avatars.First().gameObject;
                    Evaluate();
                }
#endif
            }

            if (_avatar != null)
            {
                if (_shadersWithGrabpass == null) Evaluate();
                if (_skinendMeshesWithBlendshapes == null) Evaluate();
                EditorGUILayout.Space();
                DrawLine(1);
                //VRAM

                if(DrawSection("VRAM", ToMebiByteString(_vramSize), false))
                    TextureVRAM.Init(_avatar);

                Rect r;

                //Grabpasses
                _grabpassFoldout = DrawSection("Grabpasses", _grabpassCount.ToString(), _grabpassFoldout);
                if(_grabpassFoldout)
                {
                    DrawGrabpassFoldout();
                }
                //Blendshapes
                _blendshapeFoldout = DrawSection("Blendshapes", _totalBlendshapeVerticies.ToString(), _blendshapeFoldout);
                if(_blendshapeFoldout)
                {
                    DrawBlendshapeFoldout();
                }

                // Any states
                _anyStateFoldout = DrawSection("Any State Transitions", _anyStateTransitions.ToString(), _anyStateFoldout);
                if(_anyStateFoldout)
                {
                    using(new DetailsFoldout("For each any state transition the conditons are checked every frame. " +
                        "This makes them expensive compared to normal transitions and a large number of them can seriously impact the CPU usage. A healty limit is around 50 transitions."))
                        {

                        }
                }

                // Layer count
                _layerCountFoldout = DrawSection("Layer Count", _layerCount.ToString(), _layerCountFoldout);
                if(_layerCountFoldout)
                {
                    using(new DetailsFoldout("The more layers you have the more expensive the animator is to run. " +
                        "Animators run on the CPU, so in a CPU-limited game like VRC the smaller the layer count, the better."))
                        {

                        }
                }

                EditorGUILayout.Space();
                DrawLine(1);

                //Write defaults
                r = GUILayoutUtility.GetRect(new GUIContent(), EditorStyles.boldLabel);
                GUI.Label(r, "Write Defaults: ", EditorStyles.boldLabel);
                r.x += 140;
                GUI.Label(r, "" + _writeDefault);
                EditorGUILayout.HelpBox("Unity needs all the states in your animator to have the same write default value: Either all off or all on. "+
                    "If a state is marked with write defaults it means that the values animated by this state will be set to their default values when not in this state. " +
                    "This can be useful to make compact toggles, but is very prohibiting when making more complex systems." +
                    "Click here for more information on animator states.", MessageType.None);
                if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                    Application.OpenURL("https://docs.unity3d.com/Manual/class-State.html");
                if (_writeDefaultoutliers.Length > 0)
                {
                    EditorGUILayout.HelpBox("Not all of your states have the same write default value.", MessageType.Warning);
                    _writeDefaultsFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_writeDefaultsFoldout, "Outliers", EditorStyles.foldout);
                    if (_writeDefaultsFoldout)
                    {
                        foreach (string s in _writeDefaultoutliers)
                            EditorGUILayout.LabelField(s);
                    }
                    EditorGUILayout.EndFoldoutHeaderGroup();
                }

                EditorGUILayout.Space();
                DrawLine(1);

                //Empty states
                r = GUILayoutUtility.GetRect(new GUIContent(), EditorStyles.boldLabel);
                GUI.Label(r, "Empty States: ", EditorStyles.boldLabel);
                r.x += 140;
                GUI.Label(r, "" + _emptyStates.Length);
                if (_emptyStates.Length > 0)
                {
                    EditorGUILayout.HelpBox("Some of your states do not have a motion. This might cause issues. " +
                        "You can place an empty animation clip in them to prevent this.", MessageType.Warning);
                    _emptyStatesFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_emptyStatesFoldout, "Outliers", EditorStyles.foldout);
                    if (_emptyStatesFoldout)
                    {
                        foreach (string s in _emptyStates)
                            EditorGUILayout.LabelField(s);
                    }
                    EditorGUILayout.EndFoldoutHeaderGroup();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        bool DrawSection(string header, string value, bool foldout)
        {
            EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{header}:", EditorStyles.boldLabel, GUILayout.Width(150));
                EditorGUILayout.LabelField(value, GUILayout.Width(200));
                GUILayout.FlexibleSpace();
                if(GUILayout.Button(foldout ? "Hide Details" : "Show Details", GUILayout.Width(150)))
                {
                    foldout = !foldout;
                }
            EditorGUILayout.EndHorizontal();
            return foldout;
        }

        class DetailsFoldout : GUI.Scope
        {
            public DetailsFoldout(string info)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(30);
                GUILayout.BeginVertical();
                if (string.IsNullOrWhiteSpace(info) == false)
                    EditorGUILayout.HelpBox(info, MessageType.Info);
                EditorGUILayout.Space();
            }

            protected override void CloseScope()
            {
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        class GUILayoutIndent : GUI.Scope
        {
            public GUILayoutIndent(int indent)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(indent * 15);
                GUILayout.BeginVertical();
            }

            protected override void CloseScope()
            {
                GUILayout.EndHorizontal();
            }
        }

        void DrawGrabpassFoldout()
        {
            using(new DetailsFoldout("Grabpasses are very expensive. They save your whole screen at a certain point in the rendering process to use it as a texture in the shader."))
            {
                if (_grabpassCount > 0)
                {
                    foreach (Shader s in _shadersWithGrabpass)
                        EditorGUILayout.ObjectField(s, typeof(Shader), false);
                }
            }
        }

        void DrawBlendshapeFoldout()
        {
            using(new DetailsFoldout("The performance impact of blendshapes grows linearly with polygon count. The general consensus is that above 32,000 triangles splitting your mesh will improve performance." +
                    " Click here for more information on blendshapes from the VRChat Documentation."))
            {
                if(Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                    Application.OpenURL("https://docs.vrchat.com/docs/avatar-optimizing-tips#-except-when-youre-using-shapekeys");

                    EditorGUILayout.BeginHorizontal(GUI.skin.box);
                            EditorGUILayout.LabelField("Blendshape Triangles: ", _totalBlendshapeVerticies.ToString());    
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal(GUI.skin.box);
                            EditorGUILayout.LabelField("#Meshes: ", _skinendMeshesWithBlendshapes.Length.ToString());    
                    EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                if (_skinendMeshesWithBlendshapes.Count() > 0 && _skinendMeshesWithBlendshapes.First().Item2 > 32000)
                    EditorGUILayout.HelpBox($"Consider splitting \"{_skinendMeshesWithBlendshapes.First().Item1.name}\" into multiple meshes where only one has blendshapes. " +
                        $"This will reduce the amount polygons actively affected by blendshapes.", MessageType.Error);

                EditorGUILayout.Space();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Skinned Mesh Renderer");
                EditorGUILayout.LabelField("Affected Triangles");
                EditorGUILayout.EndHorizontal();
                foreach ((SkinnedMeshRenderer, int, int) mesh in _skinendMeshesWithBlendshapes)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(mesh.Item1, typeof(SkinnedMeshRenderer), true);
                    EditorGUILayout.LabelField($"=> {mesh.Item2} triangles.");
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
        
        void DrawLine(int i_height)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, i_height);
            rect.height = i_height;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
        }

        void Evaluate()
        {
            _vramSize = TextureVRAM.QuickCalc(_avatar);
            IEnumerable<Material> materials = GetMaterials(_avatar)[1];
            IEnumerable<Shader> shaders = materials.Where(m => m!= null && m.shader != null).Select(m => m.shader).Distinct();
            _shadersWithGrabpass = shaders.Where(s => File.Exists(AssetDatabase.GetAssetPath(s)) &&  Regex.Match(File.ReadAllText(AssetDatabase.GetAssetPath(s)), @"GrabPass\s*{\s*""(\w|_)+""\s+}").Success ).ToArray();
            _grabpassCount = _shadersWithGrabpass.Count();
#if CVR_CCK_EXISTS
            CVRAvatar descriptor = _avatar.GetComponent<CVRAvatar>();

            AnimatorController controller = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(descriptor.overrides.runtimeAnimatorController));

            IEnumerable<AnimatorControllerLayer> layers = controller.layers.Where(l => l != null);
            IEnumerable<AnimatorStateMachine> statesMachines = layers.Select(l => l.stateMachine).Where(s => s != null);
            _anyStateTransitions = statesMachines.SelectMany(l => l.anyStateTransitions).Count();
            IEnumerable<(AnimatorState,string)> states = statesMachines.SelectMany(m => m.states.Select(s => (s.state, m.name+"/"+s.state.name)));

            _emptyStates = states.Where(s => s.Item1.motion == null).Select(s => s.Item2).ToArray();

            IEnumerable<(AnimatorState, string)> wdOn = states.Where(s => s.Item1.writeDefaultValues);
            IEnumerable<(AnimatorState, string)> wdOff = states.Where(s => !s.Item1.writeDefaultValues);
            _writeDefault = wdOn.Count() >= wdOff.Count();
            if (_writeDefault) _writeDefaultoutliers = wdOff.Select(s => s.Item2).ToArray();
            else _writeDefaultoutliers = wdOn.Select(s => s.Item2).ToArray();

            _layerCount = layers.Count();
#endif

            _skinendMeshesWithBlendshapes =  _avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).Select(r => (r, r.sharedMesh.triangles.Length / 3, r.sharedMesh.blendShapeCount)).OrderByDescending(i => i.Item2).ToArray();
            _totalBlendshapeVerticies = _skinendMeshesWithBlendshapes.Sum(i => i.verticies);
        }

        public static IEnumerable<Material>[] GetMaterials(GameObject avatar)
        {
            IEnumerable<Renderer> allBuiltRenderers = avatar.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.GetComponentsInParent<Transform>(true).All(g => g.tag != "EditorOnly"));

            List<Material> materialsActive = allBuiltRenderers.Where(r => r.gameObject.activeInHierarchy).SelectMany(r => r.sharedMaterials).ToList();
            List<Material> materialsAll = allBuiltRenderers.SelectMany(r => r.sharedMaterials).ToList();
#if CVR_CCK_EXISTS
            //animation materials
            CVRAvatar descriptor = avatar.GetComponent<CVRAvatar>();

            if (descriptor != null)
            {
                AnimatorController controller = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(descriptor.overrides.runtimeAnimatorController));

                IEnumerable<AnimationClip> clips = controller.animationClips.Distinct();
                foreach (AnimationClip clip in clips)
                {
                    IEnumerable<Material> clipMaterials = AnimationUtility.GetObjectReferenceCurveBindings(clip).Where(b => b.isPPtrCurve && b.type.IsSubclassOf(typeof(Renderer)) && b.propertyName.StartsWith("m_Materials"))
                        .SelectMany(b => AnimationUtility.GetObjectReferenceCurve(clip, b)).Select(r => r.value as Material);
                    materialsAll.AddRange(clipMaterials);
                }
            }

#endif
            return new IEnumerable<Material>[] { materialsActive.Distinct(), materialsAll.Distinct() };
        }

        public static string ToByteString(long l)
        {
            if (l < 1000) return l + " B";
            if (l < 1000000) return (l / 1000f).ToString("n2") + " KB";
            if (l < 1000000000) return (l / 1000000f).ToString("n2") + " MB";
            else return (l / 1000000000f).ToString("n2") + " GB";
        }

        public static string ToMebiByteString(long l)
        {
            if (l < Math.Pow(2, 10)) return l + " B";
            if (l < Math.Pow(2, 20)) return (l / Math.Pow(2, 10)).ToString("n2") + " KiB";
            if (l < Math.Pow(2, 30)) return (l / Math.Pow(2, 20)).ToString("n2") + " MiB";
            else return (l / Math.Pow(2, 30)).ToString("n2") + " GiB";
        }

        public static string ToShortMebiByteString(long l)
        {
            if (l < Math.Pow(2, 10)) return l + " B";
            if (l < Math.Pow(2, 20)) return (l / Math.Pow(2, 10)).ToString("n0") + " KiB";
            if (l < Math.Pow(2, 30)) return (l / Math.Pow(2, 20)).ToString("n1") + " MiB";
            else return (l / Math.Pow(2, 30)).ToString("n1") + " GiB";
        }
    }
}
#endif
