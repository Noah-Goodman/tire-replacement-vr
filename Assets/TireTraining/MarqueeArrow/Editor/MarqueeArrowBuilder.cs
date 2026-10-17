using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TireTraining.Editor
{
    /// <summary>Builds persistent meshes, materials and a scene-independent prefab.</summary>
    public static class MarqueeArrowBuilder
    {
        public const string Folder = "Assets/TireTraining/MarqueeArrow";
        public const string PrefabPath = Folder + "/VegasMarqueeArrow.prefab";

        [MenuItem("Tire Training/Assets/Create Vegas Marquee Arrow")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                throw new InvalidOperationException("The prefab already exists. Move it before regenerating to preserve your edits.");
            var outline = Outline();
            var casing = Material("Charcoal enamel", new Color(.055f,.063f,.07f), .65f, .48f);
            var gold = Material("Champagne brass", new Color(.85f,.53f,.16f), .78f, .7f);
            var red = Material("Cherry red enamel", new Color(.48f,.012f,.022f), .28f, .72f);
            var lip = Material("Scarlet bevel", new Color(.72f,.025f,.018f), .5f, .65f);
            var off = Material("Bulbs OFF - amber glass", new Color(.31f,.19f,.065f), .1f, .7f);
            var on = Material("Bulbs ON - warm ivory", new Color(1f,.81f,.39f), .05f, .45f);
            on.shader = Shader.Find("Universal Render Pipeline/Unlit");
            // HDR unlit color keeps lamps luminous even in an unlit garage; supports Bloom.
            on.SetColor("_BaseColor", new Color(5f,3.1f,1f,1f));
            var root = new GameObject("Vegas Marquee Arrow");
            try
            {
                Part(root, "Dark metal housing", Extrude(outline, -.075f, .075f), casing);
                Part(root, "Brass outer rim", Extrude(Inset(outline,.025f), -.09f,.09f), gold);
                Part(root, "Raised scarlet surround", Extrude(Inset(outline,.057f), -.10f,.10f), lip);
                Part(root, "Recessed cherry face", Extrude(Inset(outline,.185f), -.108f,.108f), red);
                // Thin brass pinstripe between the lamps and the recessed red face.
                Part(root, "Fine inner brass pinstripe", Extrude(Inset(outline,.158f), -.112f,.112f), gold);
                Part(root, "Central red enamel", Extrude(Inset(outline,.171f), -.115f,.115f), red);
                var centers = Equidistant(Inset(outline,.105f), .145f);
                var socketMeshes = new List<CombineInstance>();
                var bulbMeshes = new List<CombineInstance>();
                var sphereMesh = BulbSphere();
                foreach (var p in centers)
                    foreach (int side in new[]{-1,1})
                    {
                        socketMeshes.Add(new CombineInstance { mesh = sphereMesh, transform = Matrix4x4.TRS(new Vector3(p.x,p.y,side*.107f),Quaternion.identity,new Vector3(.093f,.093f,.034f)) });
                        bulbMeshes.Add(new CombineInstance { mesh = sphereMesh, transform = Matrix4x4.TRS(new Vector3(p.x,p.y,side*.13f),Quaternion.identity,new Vector3(.068f,.068f,.052f)) });
                    }
                Part(root,"Brass bulb sockets", Combine(socketMeshes),gold);
                var bulbs = Part(root,"Warm marquee bulbs",Combine(bulbMeshes),off);
                UnityEngine.Object.DestroyImmediate(sphereMesh);
                var controller = root.AddComponent<MarqueeArrowLights>();
                var so = new SerializedObject(controller);
                so.FindProperty("bulbRenderers").arraySize = 1;
                so.FindProperty("bulbRenderers").GetArrayElementAtIndex(0).objectReferenceValue = bulbs;
                so.FindProperty("litMaterial").objectReferenceValue = on;
                so.FindProperty("unlitMaterial").objectReferenceValue = off;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Created {PrefabPath}: {centers.Count} bulbs per side, lights initially OFF.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Material Material(string name, Color color, float metallic, float smoothness)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor",color); m.SetFloat("_Metallic",metallic); m.SetFloat("_Smoothness",smoothness);
            AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat"); return m;
        }

        internal static MeshRenderer Part(GameObject root,string name,Mesh mesh,Material material,string folder = Folder)
        {
            mesh.name = name; AssetDatabase.CreateAsset(mesh,folder+"/"+name+".asset");
            var go = new GameObject(name); go.transform.SetParent(root.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>(); r.sharedMaterial=material;
            r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false; return r;
        }

        // Clockwise silhouette: upright tail, rounded elbow, right-pointing arrowhead.
        private static List<Vector2> Outline()
        {
            var p=new List<Vector2>{new Vector2(-1.25f,1f),new Vector2(-.65f,1f),new Vector2(-.65f,-.05f)};
            for(int i=1;i<=12;i++) {float a=Mathf.PI+i*Mathf.PI/24; p.Add(new Vector2(-.35f,-.05f)+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.30f);}
            p.AddRange(new[]{new Vector2(.45f,-.35f),new Vector2(.45f,.10f),new Vector2(1.45f,-.65f),new Vector2(.45f,-1.40f),new Vector2(.45f,-.95f),new Vector2(-.35f,-.95f)});
            for(int i=1;i<=24;i++) {float a=-Mathf.PI/2-i*Mathf.PI/48; p.Add(new Vector2(-.35f,-.05f)+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.90f);}
            return p;
        }
        internal static List<Vector2> Inset(List<Vector2> p,float distance)
        {
            var q=new List<Vector2>();
            for(int i=0;i<p.Count;i++)
            {
                var a=(p[i]-p[(i+p.Count-1)%p.Count]).normalized; var b=(p[(i+1)%p.Count]-p[i]).normalized;
                var n1=new Vector2(a.y,-a.x); var n2=new Vector2(b.y,-b.x); var bisector=(n1+n2).normalized;
                q.Add(p[i]+bisector*(distance/Vector2.Dot(bisector,n1)));
            }
            return q;
        }
        internal static Mesh Extrude(List<Vector2> p,float front,float back)
        {
            var v=new List<Vector3>(); var t=new List<int>(); int n=p.Count;
            foreach(var a in p) v.Add(new Vector3(a.x,a.y,front));
            foreach(var a in p) v.Add(new Vector3(a.x,a.y,back));
            var remaining=new List<int>(); for(int i=0;i<n;i++) remaining.Add(i);
            int guard=0;
            while(remaining.Count>2 && guard++<n*n)
            {
                bool clipped=false;
                for(int j=0;j<remaining.Count;j++)
                {
                    int a=remaining[(j+remaining.Count-1)%remaining.Count],b=remaining[j],c=remaining[(j+1)%remaining.Count];
                    if(Cross(p[b]-p[a],p[c]-p[b])>=-0.000001f) continue;
                    bool contains=false;
                    foreach(int k in remaining) if(k!=a && k!=b && k!=c && Inside(p[k],p[a],p[b],p[c])) {contains=true;break;}
                    if(contains) continue;
                    t.AddRange(new[]{a,b,c,c+n,b+n,a+n}); remaining.RemoveAt(j);clipped=true;break;
                }
                if(!clipped) throw new InvalidOperationException("Arrow triangulation failed.");
            }
            for(int i=0;i<n;i++){int j=(i+1)%n;int s=v.Count;v.AddRange(new[]{v[i],v[j],v[j+n],v[i+n]});t.AddRange(new[]{s,s+2,s+1,s,s+3,s+2});}
            var mesh=new Mesh();mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private static bool Inside(Vector2 p,Vector2 a,Vector2 b,Vector2 c)=>Cross(b-a,p-a)<=0 && Cross(c-b,p-b)<=0 && Cross(a-c,p-c)<=0;
        internal static List<Vector2> Equidistant(List<Vector2> p,float spacing)
        {
            float length=0;for(int i=0;i<p.Count;i++)length+=Vector2.Distance(p[i],p[(i+1)%p.Count]);
            int count=Mathf.RoundToInt(length/spacing);var result=new List<Vector2>();
            for(int k=0;k<count;k++){float d=k*length/count;for(int i=0;i<p.Count;i++){float segment=Vector2.Distance(p[i],p[(i+1)%p.Count]);if(d<=segment){result.Add(Vector2.Lerp(p[i],p[(i+1)%p.Count],d/segment));break;}d-=segment;}}
            return result;
        }
        // Small lamps need only 12 radial segments, instead of Unity's high-density primitive.
        internal static Mesh BulbSphere()
        {
            const int rings=8, segments=12;
            var v=new List<Vector3>(); var t=new List<int>();
            for(int y=0;y<=rings;y++) for(int x=0;x<=segments;x++)
            {
                float a=y*Mathf.PI/rings, b=x*2*Mathf.PI/segments;
                v.Add(new Vector3(Mathf.Sin(a)*Mathf.Cos(b),Mathf.Cos(a),Mathf.Sin(a)*Mathf.Sin(b))*.5f);
            }
            for(int y=0;y<rings;y++) for(int x=0;x<segments;x++)
            {
                int i=y*(segments+1)+x, j=i+segments+1;
                t.AddRange(new[]{i,i+1,j,i+1,j+1,j});
            }
            var m=new Mesh();m.SetVertices(v);m.SetTriangles(t,0);m.RecalculateNormals();return m;
        }
        internal static Mesh Combine(List<CombineInstance> parts){var m=new Mesh {indexFormat=IndexFormat.UInt32};m.CombineMeshes(parts.ToArray());return m;}
    }
}
