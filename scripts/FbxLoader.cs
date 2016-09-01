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


        private static ErrorCode lastError = ErrorCode.LoadOk;
        public static ErrorCode LoadFbx(string name, out GameObject fbx)
        {
            //fbx = new GameObject("fbxLoader");
            fbx = null;
            lastError = ErrorCode.LoadOk;

            if (!initialized)
                return ErrorCode.NotInitialized;

            if (!File.Exists(name))
            {
                return ErrorCode.OpenFileError;
            }

            try
            {
                var scenenode = _LoadFbx(name);                
                CreateSceneObject(scenenode, out fbx);
                //if (fbx.transform.childCount == 1)
                //{
                //    var child = fbx.transform.GetChild(0);
                //    child.parent = null;
                //    var oldfbx = fbx;
                //    fbx = child.gameObject;
                //    GameObject.Destroy(oldfbx);
                //}
            }
            catch(FbxLoaderException e)
            {
                return e.Error;
            }
            fbx.name = GetFileName(name);

            return lastError;
        }


        static string GetFileName(string path)
        {
            int index = path.LastIndexOf("\\");
            if (index >= 0 && index < path.Length)
            {
                string name = path.Substring(index + 1);
                if (name.Length > 4)
                    name = name.Substring(0, name.Length - 4);
                return name;
            }
            return "GameObject";
        }

        public static ErrorCode LoadFbx(byte[] filestream,out GameObject fbx)
        {
            fbx = null;//new GameObject("fbxLoader");
            lastError = ErrorCode.LoadOk;
            if (!initialized)
                return ErrorCode.NotInitialized;
            try
            {
                var scenenode = _LoadFbx(filestream);
                CreateSceneObject(scenenode, out fbx);
                if (fbx.transform.childCount == 1)
                {
                    var child = fbx.transform.GetChild(0);
                    child.parent = null;
                    fbx = child.gameObject;
                }
            }
            catch (FbxLoaderException e)
            {
                return e.Error;
            }
            return lastError;
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


        static void CreateSceneObject(fbxImporter.USceneNode scene,out GameObject fbx) 
        {
            //fbx.transform.localRotation = Quaternion.Euler(270, 0, 0);
            //fbx.name = "name";         
            fbx = null;
            if (scene.meshNums == 1)
            {
                fbx = CreateMesh(scene.meshList[0], fbx);
            } 
            else
            {
                fbx = new GameObject(scene.meshList[0].name);
                fbx.transform.localRotation = Quaternion.Euler(270, 0, 0);
                for (int i = 0; i < scene.meshNums; ++i)
                {
                    var child = CreateMesh(scene.meshList[i], fbx);
                    if (child != null)
                    {
                        child.transform.parent = fbx.transform;
                    }
                }
            }
        }

        static Quaternion MatrixToQuaternion(double[,] mat)
        {
            var matrix = Matrix4x4.zero;
            matrix.m00 = (float)mat[0, 0];
            matrix.m01 = (float)mat[0, 1];
            matrix.m02 = (float)mat[0, 2];
            matrix.m10 = (float)mat[1, 0];
            matrix.m11 = (float)mat[1, 1];
            matrix.m12 = (float)mat[1, 2];
            matrix.m20 = (float)mat[2, 0];
            matrix.m21 = (float)mat[2, 1];
            matrix.m22 = (float)mat[2, 2];

            return MatrixToQuaternion(matrix);
        }

        static Quaternion MatrixToQuaternion(Matrix4x4 mat)
        {
            mat = mat.transpose;
            double w = 0, x = 0, y = 0, z = 0;

            double fourWSquaredMinus1 = mat.m00 + mat.m11 + mat.m22;
            double fourXSquaredMinus1 = mat.m00 - mat.m11 - mat.m22;
            double fourYSquaredMinus1 = mat.m11 - mat.m00 - mat.m22;
            double fourZSquaredMinus1 = mat.m22 - mat.m00 - mat.m11;

            int biggestIndex = 0;
            double fourBiggestSquaredMinus1 = fourWSquaredMinus1;
            if (fourXSquaredMinus1 > fourBiggestSquaredMinus1)
            {
                fourBiggestSquaredMinus1 = fourXSquaredMinus1;
                biggestIndex = 1;
            }
            if (fourYSquaredMinus1 > fourBiggestSquaredMinus1)
            {
                fourBiggestSquaredMinus1 = fourYSquaredMinus1;
                biggestIndex = 2;
            }
            if (fourZSquaredMinus1 > fourBiggestSquaredMinus1)
            {
                fourBiggestSquaredMinus1 = fourZSquaredMinus1;
                biggestIndex = 3;
            }

            // Perform square root and division
            double biggestVal = (double)Math.Sqrt(fourBiggestSquaredMinus1 + 1.0f) * 0.5f;
            double mult = 0.25 / biggestVal;
            // Apply table to compute quaternion values
            switch (biggestIndex)
            {
                case 0:
                    w = biggestVal;
                    x = (mat.m12 - mat.m21) * mult;
                    y = (mat.m20 - mat.m02) * mult;
                    z = (mat.m01 - mat.m10) * mult;
                    break;
                case 1:
                    x = biggestVal;
                    w = (mat.m12 - mat.m21) * mult;
                    y = (mat.m01 + mat.m10) * mult;
                    z = (mat.m20 + mat.m02) * mult;
                    break;
                case 2:
                    y = biggestVal;
                    w = (mat.m20 - mat.m12) * mult;
                    x = (mat.m01 + mat.m10) * mult;
                    z = (mat.m12 + mat.m21) * mult;
                    break;
                case 3:
                    z = biggestVal;
                    w = (mat.m01 - mat.m10) * mult;
                    x = (mat.m20 + mat.m02) * mult;
                    y = (mat.m12 + mat.m21) * mult;
                    break;
            }
            return new Quaternion((float)x, (float)y, (float)z, (float)w); 
        }

        static double[,] QuaternionToMat(Quaternion q)
        {
            double x = q.x;
            double y = q.y;
            double z = q.z;
            double w = q.w;

            var mat = new double[3,3];
            mat[0,0] = 1 - 2 * y * y - 2 * z * z;
            mat[0,1] = 2 * x * y + 2 * w * z; 
            mat[0,2] = 2 * x * z - 2 * w * y;
            mat[1,0] = 2 * x * y - 2 * w * z;
            mat[1,1] = 1 - 2 * x * x - 2 * z * z;
            mat[1,2] = 2 * y * z + 2 * w *  x;
            mat[2,0] = 2 * x * z + 2 * w * y;
            mat[2,1] = 2 * y * z - 2 * w * x;
            mat[2,2] = 1 - 2 * x * x - 2 * y * y;
            return mat;
        }

        static GameObject CreateMesh(fbxImporter.UMeshNode meshNode,GameObject parent)
        {
            GameObject go = new GameObject();
            Debug.Log(meshNode.name);
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            // Debug.Log(FbxVector3ToVector3(meshNode.geoTras.localrotation));
            //Debug.Log(FbxVector3ToVector3(meshNode.geoTras.localscaling));

            go.name = meshNode.name;
            var localT = FbxVector3ToVector3(meshNode.geoTras.localtranslation);
            var localR = FbxVector4ToQuaternion(meshNode.geoTras.localrotation);
            var localS = FbxVector3ToVector3(meshNode.geoTras.localscaling);

            Matrix4x4 trsMat = Matrix4x4.TRS(localT, localR, new Vector3(1,1,1));
            //Debug.Log(localR + "   " + localR.eulerAngles);
            //var r2 = MatrixToQuaternion(QuaternionToMat(localR));
            //Debug.Log(r2 + "    " + r2.eulerAngles);
            //Debug.Log(MatrixToQuaternion(trsMat)  + "   " + MatrixToQuaternion(trsMat).eulerAngles);

            Matrix4x4 T = new Matrix4x4();
            T.m00 = -1; T.m01 =  0; T.m02 = 0; T.m03 = 0;
            T.m10 =  0; T.m11 =  1; T.m12 = 0; T.m13 = 0;
            T.m20 =  0; T.m21 =  0; T.m22 = 1; T.m23 = 0;
            T.m30 =  0; T.m31 =  0; T.m32 = 0; T.m33 = 1;
            Matrix4x4 newTrs = T.transpose * trsMat * T;
            Vector3 position = newTrs.GetColumn(3);
            // Extract new local rotation
            Quaternion rotationLook = Quaternion.LookRotation(
                newTrs.GetColumn(2),
                newTrs.GetColumn(1)
            );
            //Debug.Log(rotationLook);
            //Quaternion rotation = MatrixToQuaternion(newTrs);

            // Extract new local scale
            // Vector3 scale = new Vector3(
                // newTrs.GetColumn(0).magnitude,
                // newTrs.GetColumn(1).magnitude,
                // newTrs.GetColumn(2).magnitude
            // );

            go.transform.localPosition = position;
            go.transform.localRotation = rotationLook;
            go.transform.localScale = localS;

            if (parent == null)
            {
                var q = go.transform.localRotation.eulerAngles;
                if (Math.Abs(q.x - 270) > 1.0f)
                {
                    var tmp = position;
                    position.y = tmp.z;
                    position.z = -tmp.y;
                }
                q .x = 270;
                go.transform.localRotation = Quaternion.Euler(q);
                go.transform.localPosition = position;
            }
            

            //Debug.Log(newTrs);


            //Debug.Log("------------------------");
            //Debug.Log(localT);
            //Debug.Log(position);
            //Debug.Log(localR);
            //Debug.Log(rotation.eulerAngles);
            //Debug.Log(FbxVector3ToVector3(meshNode.geoTras.localscaling));
            //Debug.Log(scale);



            //localT.x = -localT.x;


            //if (Math.Abs(localR.y) > 0.01f)
            //{
            //    var t = localR;
            //    localR.x = t.z;
            //    localR.z = t.x;
            //}
            //else
            //{
            //    localR.z = -localR.z;
            //}

            //if (parent != null)
            //{
            //    localT = localT + parent.transform.localPosition;
            //    localR = localR + parent.transform.localRotation.eulerAngles;
            //    localS.x = localS.x  * parent.transform.localScale.x;
            //    localS.y = localS.y * parent.transform.localScale.y;
            //    localS.z = localS.z * parent.transform.localScale.z;
            //}

            //go.transform.localScale = localS;
            //go.transform.localRotation = Quaternion.Euler(localR);
            //go.transform.localPosition = localT;

            //Debug.Log("------------------------");
            //Debug.Log(localT);
            //Debug.Log(go.transform.localPosition);
            //Debug.Log(localR);
            //Debug.Log(go.transform.localRotation.eulerAngles);
            //Debug.Log(FbxVector3ToVector3(meshNode.geoTras.localscaling));
            //Debug.Log(go.transform.localScale);

            if (meshNode.Polygons != null)
            {
                var meshfilter = go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                var mesh = meshfilter.mesh;
                mesh.name = meshNode.name;



                int vcout = 0;
                mesh.SetVertices(GetAllVertexs(meshNode.Polygons, meshNode.geoTras, out vcout));
                //mesh.SetNormals(GetAllNormals(meshNode.Polygons, meshNode.geoTras));
                var uvs = GetAllUVs(meshNode.Polygons);
                mesh.SetUVs(0, uvs);
                if (uvs.Count != vcout)
                {
                    //GameObject.Destroy(go);
                    //throw new FbxLoaderException(ErrorCode.UnsetPointUV);                    
                    //Debug.LogError("顶点没有UV");
                    lastError = ErrorCode.UnsetPointUV;
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
                var child = CreateMesh(meshNode.children[i],go);
                if (child != null)
                {
                    //child.transform.parent = go.transform;
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
                if (!File.Exists(texInfo[i].diffusePath))
                {
                    //Debug.LogError("texInfo[i].diffusePath 文件不存在");
                    lastError = ErrorCode.TextureNotFound;
                    continue;
                }
                if (!tex.LoadImage(File.ReadAllBytes(texInfo[i].diffusePath)))
                {
                    //Debug.LogError("texInfo[i].diffusePath 文件加载失败");
                    lastError = ErrorCode.TextureNotFound;
                    continue;
                    //throw new FbxLoaderException(ErrorCode.TextureNotFound);
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
                        pos.x *= geo.scaling.x;
                        pos.y *= geo.scaling.y;
                        pos.z *= geo.scaling.z;
                        //pos = pos + Quaternion.Euler(FbxVector3ToVector3(geo.rotation)) * pos;
                        pos += FbxVector3ToVector3(geo.translation);
                        pos.x = -pos.x;
                        vertexs.Add(pos);
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
                        //pos = Quaternion.Euler(FbxVector3ToVector3(geo.rotation)) * pos;
                        pos.x = -pos.x;
                        vertexs.Add(pos.normalized);
                    }
                }
            }
            return vertexs;
        }

        static Vector3 FbxVector3ToVector3(fbxImporter.Vector3 vec)
        {
            return new Vector3(vec.x, vec.y, vec.z);
        }

        static Quaternion FbxVector4ToQuaternion(fbxImporter.Vector4 vec)
        {
            return new Quaternion(vec.x, vec.y, vec.z,vec.w);
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