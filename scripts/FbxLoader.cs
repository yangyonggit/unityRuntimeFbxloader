using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;
using System;
using System.IO;
using System.Collections.Generic;


namespace Jhqc.UnityFbxLoader
{
    struct USceneInfo
    {
        public int sizeofbin;
        public IntPtr bins;
    }

    public enum ErrorCode
    {
        LoadOk,
        NotInitialized,
        OpenFileError,
        UnsetPointUV,
        StreamDecodeError,
        TextureNotFound,
        Unknow,
    }

    

    public class FbxLoader
    {
        class FbxLoaderException : System.Exception
        {
            internal FbxLoaderException(ErrorCode e)
            {
                error = e;
            }

            private ErrorCode error;

            internal ErrorCode Error
            {
                get { return error; }
            }
        }

        private static bool initialized = false;

        public static ErrorCode LoadFbx(string name, ref GameObject fbx)
        {
            if (!initialized)
                return ErrorCode.NotInitialized;

            if (!File.Exists(name))
            {
                return ErrorCode.OpenFileError;
            }

            try
            {
                var scenenode = _LoadFbx(name);                
                CreateSceneObject(scenenode, ref fbx);
            }
            catch(FbxLoaderException e)
            {
                return e.Error;
            }
            string filename = name;
            if (filename.Length > 4)
                fbx.name = filename.Substring(0, filename.Length - 4);

            return ErrorCode.LoadOk;
        }

        public static ErrorCode LoadFbx(byte[] filestream,ref GameObject fbx)
        {
            if (!initialized)
                return ErrorCode.NotInitialized;
            try
            {
                var scenenode = _LoadFbx(filestream);
                CreateSceneObject(scenenode, ref fbx);
            }
            catch (FbxLoaderException e)
            {
                return e.Error;
            }
            return ErrorCode.LoadOk;
        }

        public static void InitLoader()
        {
            InitFbxLoader();
            initialized = true;
        }

        public static void UnInitFbxLoader()
        {
            initialized = false;
            FbxLoaderExit();            
        }



        static T Decode<T>(byte[] reply) where T : global::ProtoBuf.IExtensible
        {
            T data = default(T);
            using (MemoryStream ms = new MemoryStream(reply))
            {
                try
                {
                    data = ProtoBuf.Serializer.Deserialize<T>(ms);
                }
                catch (System.Exception e)
                {
                    //data = default(T);
                    throw new FbxLoaderException(ErrorCode.StreamDecodeError);
                }
            }
            return data;
        }


        static fbxImporter.USceneNode _LoadFbx(string name)
        {
            fbxImporter.USceneNode sceneNode = null;
            USceneInfo sceneInfo = new USceneInfo();
            //IntPtr csPtr = Marshal.AllocHGlobal(Marshal.SizeOf(sceneInfo));
            IntPtr scene = LoadScene(name);
            if (scene != IntPtr.Zero)
            {
                try
                {
                    sceneInfo = (USceneInfo)Marshal.PtrToStructure(scene, typeof(USceneInfo));
                }
                catch(System.Exception e)
                {                    
                    throw new FbxLoaderException(ErrorCode.StreamDecodeError);
                }
                byte[] bins = new byte[sceneInfo.sizeofbin];
                Marshal.Copy(sceneInfo.bins, bins, 0, sceneInfo.sizeofbin);
                sceneNode = Decode<fbxImporter.USceneNode>(bins);
                ClearUSceneBin(scene);
            }

            return sceneNode;
        }

        static fbxImporter.USceneNode _LoadFbx(byte[] bins)
        {
            fbxImporter.USceneNode sceneNode = null;
            USceneInfo sceneInfo = new USceneInfo();
            //IntPtr csPtr = Marshal.AllocHGlobal(Marshal.SizeOf(sceneInfo));
            IntPtr scene = LoadSceneFromMemory(bins, bins.Length);
            if (scene != IntPtr.Zero)
            {
                try
                {
                    sceneInfo = (USceneInfo)Marshal.PtrToStructure(scene, typeof(USceneInfo));
                }
                catch (System.Exception e)
                {
                    //Marshal.FreeHGlobal(csPtr);
                    throw new FbxLoaderException(ErrorCode.StreamDecodeError);
                }
                byte[] localbins = new byte[sceneInfo.sizeofbin];
                Marshal.Copy(sceneInfo.bins, localbins, 0, sceneInfo.sizeofbin);
                sceneNode = Decode<fbxImporter.USceneNode>(localbins);
                ClearUSceneBin(scene);
            }
            return sceneNode;
        }


        static void CreateSceneObject(fbxImporter.USceneNode scene,ref GameObject fbx) 
        {
            fbx.name = "name";
            for (int i = 0; i < scene.meshNums; ++i)
            {
                var child = CreateMesh(scene.meshList[i]);
                if (child != null)
                {
                    child.transform.parent = fbx.transform;
                }
            }       
        }


        static GameObject CreateMesh(fbxImporter.UMeshNode meshNode)
        {
            GameObject go = new GameObject();
            go.name = meshNode.name;
            
            if (meshNode.Polygons != null)
            {
                var meshfilter = go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                var mesh = meshfilter.mesh;
                mesh.name = meshNode.name; 

                int vcout = 0;
                mesh.SetVertices(GetAllVertexs(meshNode.Polygons, meshNode.geoTras, out vcout));
                mesh.SetNormals(GetAllNormals(meshNode.Polygons, meshNode.geoTras));
                var uvs = GetAllUVs(meshNode.Polygons);
                mesh.SetUVs(0, uvs);
                if (uvs.Count != vcout)
                {
                    GameObject.Destroy(go);
                    throw new FbxLoaderException(ErrorCode.UnsetPointUV);                    
                }
                SetTriangle(mesh, meshNode.Polygons, meshNode.materialList);
                var texList = meshNode.textureList;
                RemoveUnusedMaterial(meshNode.materialList, ref texList);
                CreateMaterial(go.GetComponent<MeshRenderer>(), texList.ToArray());
                mesh.RecalculateNormals();
                mesh.Optimize();
            }

            for (int i = 0; i < meshNode.childrenNums; ++i)
            {
                var child = CreateMesh(meshNode.children[i]);
                if (child != null)
                {
                    child.transform.parent = go.transform;
                }
            }
            return go;
        }

        static void RemoveUnusedMaterial(fbxImporter.UMaterialIndex matList, ref List<fbxImporter.UTextureInfo> texList)
        {
            for (int i = 0; i < texList.Count; ++i)
            {
                if (!IsTextureUsed(texList[i].materialId, matList))
                {
                    texList.Remove(texList[i]);
                }
            }
        }

        static bool IsTextureUsed(int id, fbxImporter.UMaterialIndex matList)
        {
            bool bUsed = false;
            for (int i = 0; i < matList.indexList.Count; ++i)
            {
                if (id == matList.indexList[i])
                {
                    bUsed = true; break;
                }
            }
            return bUsed;
        }

        static void CreateMaterial(MeshRenderer render, fbxImporter.UTextureInfo[] texInfo)
        {
            if (texInfo.Length == 0)
                return;
            Dictionary<int, Material> dic = new Dictionary<int, Material>();
            for (int i = 0; i < texInfo.Length; ++i)
            {
                var shader = Shader.Find("Standard");
                var mat = new Material(shader);

                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(File.ReadAllBytes(texInfo[i].diffusePath)))
                {
                    throw new FbxLoaderException(ErrorCode.TextureNotFound);
                }
                if (tex.format == TextureFormat.ARGB32)
                    mat.EnableKeyword("_ALPHATEST_ON");

                mat.mainTexture = tex;
                dic.Add(texInfo[i].materialId, mat);
            }

            var matlist = SortMaterial(dic);
            render.sharedMaterials = matlist;
        }

        static Material[] SortMaterial(Dictionary<int, Material> dic)
        {
            var list = new List<KeyValuePair<int, Material>>(dic);
            list.Sort(delegate (KeyValuePair<int, Material> a, KeyValuePair<int, Material> b)
            {
                return a.Key.CompareTo(b.Key);
            }
            );

            var mats = new Material[list.Count];
            for (int i = 0; i < mats.Length; ++i)
                mats[i] = list[i].Value;

            return mats;
        }

        static int[] GetTriangle(fbxImporter.UPolygons polygons)
        {
            if (polygons.polygon != null && polygons.polygon.Count > 0)
            {
                List<int> indexList = new List<int>();
                int baseIndex = 0;
                for (int i = 0; i < polygons.polygon.Count; ++i)
                {
                    int n = polygons.polygon[i].coordinates.Count;
                    GenIndex(baseIndex, n, indexList);
                    baseIndex += n;
                }
                //indexList.Reverse();            
                return indexList.ToArray();
            }
            return null;
        }

        static void SetTriangle(Mesh mesh, fbxImporter.UPolygons polygons,
            fbxImporter.UMaterialIndex matList)
        {
            if (polygons.polygon == null || polygons.polygon.Count == 0)
            {
                return;
            }

            List<int> matindexList;
            if (matList != null)
                matindexList = matList.indexList;
            else
                matindexList = new List<int>();

            if (matindexList.Count != polygons.polygon.Count)
            {
                matindexList = new List<int>();
                for (int i = 0; i < polygons.polygon.Count; ++i)
                    matindexList.Add(0);
            }

            var dicTriangle = new Dictionary<int, List<int>>();
            int baseIndex = 0;
            for (int i = 0; i < polygons.polygon.Count; ++i)
            {
                List<int> indexlist = null;
                if (!dicTriangle.TryGetValue(matindexList[i], out indexlist))
                {
                    indexlist = new List<int>();
                    dicTriangle.Add(matindexList[i], indexlist);
                }
                int n = polygons.polygon[i].coordinates.Count;
                GenIndex(baseIndex, n, indexlist);
                baseIndex += n;
            }

            mesh.subMeshCount = dicTriangle.Keys.Count;

            var list = new List<KeyValuePair<int, List<int>>>(dicTriangle);
            list.Sort(delegate (KeyValuePair<int, List<int>> a, KeyValuePair<int, List<int>> b)
            {
                return a.Key.CompareTo(b.Key);
            }
            );


            for (int i = 0; i < list.Count; ++i)
            {
                mesh.SetTriangles(list[i].Value, i);
            }
        }

        static void GenIndex(int baseIndex, int n, List<int> list)
        {
            if (n < 3)
            {
                return;
            }
            int i = baseIndex;
            int j = baseIndex + 2;
            while (j < baseIndex + n)
            {
                list.Add(j - 1); list.Add(i); list.Add(j);
                ++j;
            }
        }

        static List<Vector3> GetAllVertexs(fbxImporter.UPolygons polygons, fbxImporter.UGeometricTransform geo, out int vertexCount)
        {
            List<Vector3> vertexs = new List<Vector3>();
            vertexCount = 0;
            if (polygons.polygon != null && polygons.polygon.Count > 0)
            {
                for (int i = 0; i < polygons.polygon.Count; ++i)
                {
                    for (int j = 0; j < polygons.polygon[i].coordinates.Count; ++j)
                    {
                        Vector3 pos = FbxVector3ToVector3(polygons.polygon[i].coordinates[j]);
                        pos = Quaternion.Euler(FbxVector3ToVector3(geo.rotation)) * pos;
                        pos += FbxVector3ToVector3(geo.translation);
                        pos.x *= geo.scaling.x;
                        pos.y *= geo.scaling.y;
                        pos.z *= geo.scaling.z;
                        vertexs.Add(ToUnityXYZ(pos));
                        ++vertexCount;
                    }
                }
            }

            //Debug.Log("GetAllVertexs   " + count);
            return vertexs;
        }

        static Vector3 ToUnityXYZ(Vector3 p)
        {
            var pp = Vector3.zero;
            pp.x = -p.x;
            pp.y = p.z;
            pp.z = -p.y;
            return pp;
        }

        static List<Vector2> GetAllUVs(fbxImporter.UPolygons polygons)
        {
            List<Vector2> vertexs = new List<Vector2>();
            if (polygons.polygon != null && polygons.polygon.Count > 0)
            {
                for (int i = 0; i < polygons.polygon.Count; ++i)
                {
                    for (int j = 0; j < polygons.polygon[i].texUV.Count; ++j)
                    {
                        vertexs.Add(FbxVector2ToVector2(polygons.polygon[i].texUV[j]));
                    }
                }
            }
            return vertexs;
        }

        static List<Vector3> GetAllNormals(fbxImporter.UPolygons polygons, fbxImporter.UGeometricTransform geo)
        {
            List<Vector3> vertexs = new List<Vector3>();
            if (polygons.polygon != null && polygons.polygon.Count > 0)
            {
                for (int i = 0; i < polygons.polygon.Count; ++i)
                {
                    for (int j = 0; j < polygons.polygon[i].normal.Count; ++j)
                    {
                        Vector3 pos = FbxVector3ToVector3(polygons.polygon[i].normal[j]);
                        pos = Quaternion.Euler(FbxVector3ToVector3(geo.rotation)) * pos;
                        vertexs.Add(ToUnityXYZ(pos).normalized);
                    }
                }
            }
            return vertexs;
        }

        static Vector3 FbxVector3ToVector3(fbxImporter.Vector3 vec)
        {
            return new Vector3(vec.x, vec.y, vec.z);
        }

        static Vector2 FbxVector2ToVector2(fbxImporter.Vector2 vec)
        {
            return new Vector2(vec.x, vec.y);
        }



        [DllImport("fbxImporter")]
        private static extern void InitFbxLoader();

        [DllImport("fbxImporter")]
        private static extern void FbxLoaderExit();

        [DllImport("fbxImporter")]
        private static extern IntPtr LoadScene(string n);

        [DllImport("fbxImporter")]
        private static extern IntPtr LoadSceneFromMemory(byte[] bins, long len);

        [DllImport("fbxImporter")]
        private static extern void ClearUSceneBin(IntPtr ptr);
    }


}