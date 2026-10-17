using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace TireTraining.Editor
{
    public static class MarqueeSignBuilder
    {
        public const string Folder = "Assets/TireTraining/MarqueeSign";
        public const string PrefabPath = Folder + "/VegasMarqueeSign.prefab";

        [MenuItem("Tire Training/Assets/Create Vegas Marquee Sign")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                throw new InvalidOperationException("The sign prefab already exists. Move it before regenerating to preserve your edits.");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (font == null) throw new InvalidOperationException("The project's LiberationSans SDF font is required.");
            var casing = Shared("Charcoal enamel");
            var gold = Shared("Champagne brass");
            var red = Shared("Cherry red enamel");
            var lip = Shared("Scarlet bevel");
            var off = Shared("Bulbs OFF - amber glass");
            var on = Shared("Bulbs ON - warm ivory");
            var outline = RoundedRectangle();
            var root = new GameObject("Vegas Marquee Sign");
            Mesh sphere = null;
            try
            {
                Part(root,"Dark metal housing",outline,0f,.075f,casing);
                Part(root,"Brass outer rim",outline,.025f,.09f,gold);
                Part(root,"Raised scarlet surround",outline,.057f,.10f,lip);
                Part(root,"Recessed cherry face",outline,.185f,.108f,red);
                Part(root,"Fine inner brass pinstripe",outline,.158f,.112f,gold);
                Part(root,"Central red enamel",outline,.171f,.115f,red);
                var centers = MarqueeArrowBuilder.Equidistant(MarqueeArrowBuilder.Inset(outline,.105f),.145f);
                var sockets = new List<CombineInstance>();
                var lamps = new List<CombineInstance>();
                sphere = MarqueeArrowBuilder.BulbSphere();
                foreach (var p in centers)
                    foreach (int side in new[]{-1,1})
                    {
                        sockets.Add(new CombineInstance {mesh=sphere,transform=Matrix4x4.TRS(new Vector3(p.x,p.y,side*.107f),Quaternion.identity,new Vector3(.093f,.093f,.034f))});
                        lamps.Add(new CombineInstance {mesh=sphere,transform=Matrix4x4.TRS(new Vector3(p.x,p.y,side*.13f),Quaternion.identity,new Vector3(.068f,.068f,.052f))});
                    }
                MarqueeArrowBuilder.Part(root,"Brass bulb sockets",MarqueeArrowBuilder.Combine(sockets),gold,Folder);
                var bulbs = MarqueeArrowBuilder.Part(root,"Warm marquee bulbs",MarqueeArrowBuilder.Combine(lamps),off,Folder);
                var controller = root.AddComponent<MarqueeArrowLights>();
                var so = new SerializedObject(controller);
                so.FindProperty("bulbRenderers").arraySize=1;
                so.FindProperty("bulbRenderers").GetArrayElementAtIndex(0).objectReferenceValue=bulbs;
                so.FindProperty("litMaterial").objectReferenceValue=on;
                so.FindProperty("unlitMaterial").objectReferenceValue=off;
                so.ApplyModifiedPropertiesWithoutUndo();

                var label=root.AddComponent<MarqueeSignText>();
                var front=Label(root,"Front text",font,-1);
                var back=Label(root,"Back text",font,1);
                var textSO=new SerializedObject(label);
                textSO.FindProperty("frontText").objectReferenceValue=front;
                textSO.FindProperty("backText").objectReferenceValue=back;
                textSO.ApplyModifiedPropertiesWithoutUndo();
                label.SetText("YOUR TEXT");
                front.ForceMeshUpdate(); back.ForceMeshUpdate();
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Created {PrefabPath} with editable double-sided text and {centers.Count} bulbs per side.");
            }
            finally
            {
                if(sphere != null) UnityEngine.Object.DestroyImmediate(sphere);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Material Shared(string name)
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(MarqueeArrowBuilder.Folder+"/"+name+".mat");
            if(m==null) throw new InvalidOperationException("Missing shared marquee material: "+name);
            return m;
        }
        private static void Part(GameObject root,string name,List<Vector2> outline,float inset,float depth,Material material)
        {
            var p=inset==0 ? outline : MarqueeArrowBuilder.Inset(outline,inset);
            MarqueeArrowBuilder.Part(root,name,MarqueeArrowBuilder.Extrude(p,-depth,depth),material,Folder);
        }
        private static TextMeshPro Label(GameObject root,string name,TMP_FontAsset font,int side)
        {
            var go=new GameObject(name);go.transform.SetParent(root.transform,false);
            go.transform.localPosition=new Vector3(0,0,side*.125f);
            if(side>0) go.transform.localRotation=Quaternion.Euler(0,180,0);
            var text=go.AddComponent<TextMeshPro>();
            text.font=font; text.fontSharedMaterial=font.material;
            text.color=new Color(1f,.88f,.60f,1f);
            text.fontStyle=FontStyles.Bold;
            text.alignment=TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta=new Vector2(2.10f,.88f);
            text.enableAutoSizing=true; text.fontSize=5f; text.fontSizeMin=.8f; text.fontSizeMax=5f;
            text.textWrappingMode=TextWrappingModes.Normal;
            text.overflowMode=TextOverflowModes.Truncate;
            text.richText=false;
            text.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            text.GetComponent<MeshRenderer>().receiveShadows=false;
            return text;
        }
        // Same physical width, lamp size, border widths and face depths as the arrow.
        private static List<Vector2> RoundedRectangle()
        {
            const float x=1.35f,y=.75f,r=.27f;
            var centers=new[]{new Vector2(x-r,y-r),new Vector2(x-r,-y+r),new Vector2(-x+r,-y+r),new Vector2(-x+r,y-r)};
            var points=new List<Vector2>();
            for(int corner=0;corner<4;corner++)
                for(int i=0;i<=12;i++)
                {
                    float a=(90-corner*90-i*7.5f)*Mathf.Deg2Rad;
                    points.Add(centers[corner]+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r);
                }
            return points;
        }
    }
}
