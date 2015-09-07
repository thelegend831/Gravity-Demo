﻿
namespace GravityDemo
{
    using System.Collections.Generic;
    using UnityEngine;

    [ExecuteInEditMode]
    public sealed partial class GravitationalField : MonoBehaviour
    {
        #region FIELDS
        [SerializeField, HideInInspector]
        private List<GravitationalBody> bodies = new List<GravitationalBody>();

        [SerializeField, HideInInspector]
        private List<GravitationalBeam> beams = new List<GravitationalBeam>();

        [SerializeField, HideInInspector] private int width  = 8;
        [SerializeField, HideInInspector] private int height = 8;
        [SerializeField, HideInInspector] private int depth  = 8;

        [SerializeField, HideInInspector] private ComputeShader gravitationalField;
        [SerializeField, HideInInspector] private ComputeShader gravitationalFieldVelocity;
        [SerializeField, HideInInspector] private Material      pointsMaterial;
        [SerializeField, HideInInspector] private Material      gridMaterial;

        [SerializeField] private bool drawPoints = true;
        [SerializeField] private bool drawGrid   = true;

        //private GravitationalBody.Data[] bodyData;
        private Vector4[] bodyData;
        private GravitationalBeam.Data[] beamData;

        private ComputeBuffer pointBuffer;
        private ComputeBuffer gridBuffer;
        private ComputeBuffer bodyBuffer;
        private ComputeBuffer beamBuffer;

        private int computePointPositionsKernel;
        private int computeDisplacementKernel;
        private int createGridKernel;
        private int computeVelocityKernel;
        #endregion

        #region PROPERTIES
        public int Width
        {
            get { return width; }
            set { width = Mathf.Max(1, value); }
        }

        public int Height
        {
            get { return height; }
            set { height = Mathf.Max(1, value); }
        }

        public int Depth
        {
            get { return depth; }
            set { depth = Mathf.Max(1, value); }
        }

        private int ThreadsX { get { return width  + 1; } }
        private int ThreadsY { get { return height + 1; } }
        private int ThreadsZ { get { return depth  + 1; } }

        private int PointCount
        {
            get
            {
                return (width  + 1) *
                       (height + 1) *
                       (depth  + 1);
            }
        }
        #endregion

        #region AWAKE
        private void Awake()
        {
            if (gravitationalField == null)
                gravitationalField = Resources.Load<ComputeShader>("GravitationalField");

            if (gravitationalFieldVelocity == null)
                gravitationalFieldVelocity = Resources.Load<ComputeShader>("GraviationalFieldVelocity");

            if (pointsMaterial == null)
                pointsMaterial = Resources.Load<Material>("GravitationalFieldPoints");

            if (gridMaterial == null)
                gridMaterial = Resources.Load<Material>("GravitationalFieldGrid");

            #if UNITY_EDITOR
            OnValidate();
            #endif
        }
        #endregion

        #region ON ENABLE / DISABLE
        private void OnEnable()
        {
            computePointPositionsKernel = gravitationalField.FindKernel("ComputePointPositions");
            computeDisplacementKernel   = gravitationalField.FindKernel("ComputeDisplacement");
            createGridKernel            = gravitationalField.FindKernel("CreateGrid");

            computeVelocityKernel = gravitationalFieldVelocity.FindKernel("ComputeVelocity");
        }

        private void OnDisable()
        {
            ReleaseComputeBuffer(ref pointBuffer);
            ReleaseComputeBuffer(ref gridBuffer);
            ReleaseComputeBuffer(ref bodyBuffer);
        }
        #endregion

        #region ON DESTROY
        private void OnDestroy()
        {
            Resources.UnloadAsset(pointsMaterial);
            Resources.UnloadAsset(gridMaterial);
        }
        #endregion

        #region ON RENDER OBJECT
        private void OnRenderObject()
        {
            ValidatePointBuffer();
            ValidateGridBuffer();
            ValidateBodyBuffer();

            if (bodies.Count > 0)
            {
                for (int i = 0; i < bodies.Count; ++i)
                {
                    //bodyData[i].mass     = 1;
                    //bodyData[i].position = bodies[i].transform.position;
                    bodyData[i]   = bodies[i].transform.position;
                    bodyData[i].w = bodies[i].Mass;
                }

                bodyBuffer.SetData(bodyData);
                //computeShader.Dispatch(computeDisplacementKernel, ThreadsX, ThreadsY, ThreadsZ);
            }

            gravitationalField.SetInt("body_count", bodies.Count);
            gravitationalField.Dispatch(computeDisplacementKernel, ThreadsX, ThreadsY, ThreadsZ);

            gravitationalField.Dispatch(createGridKernel, ThreadsX, ThreadsY, ThreadsZ);

            if (drawPoints)
            {
                pointsMaterial.SetPass(0);
                pointsMaterial.SetMatrix("object_to_world", transform.localToWorldMatrix);
                Graphics.DrawProcedural(MeshTopology.Points, pointBuffer.count);
            }

            if (drawGrid)
            {
                gridMaterial.SetPass(0);
                gridMaterial.SetMatrix("object_to_world", transform.localToWorldMatrix);
                Graphics.DrawProcedural(MeshTopology.Points, gridBuffer.count);
            }
        }
        #endregion

        #region METHODS
        public void AddBody()
        {
            GravitationalBody body =
            new GameObject().AddComponent<GravitationalBody>();
            body.transform.parent = transform;
            bodies.Add(body);

            #if UNITY_EDITOR
            UnityEditor.Selection.activeGameObject = body.gameObject;
            #endif
        }

        private void AddBeam()
        {
            GravitationalBeam beam =
            new GameObject().AddComponent<GravitationalBeam>();
            beam.transform.parent = transform;
            beams.Add(beam);

            #if UNITY_EDITOR
            UnityEditor.Selection.activeGameObject = beam.gameObject;
            #endif
        }

        private void ValidatePointBuffer()
        {
            if (pointBuffer == null || pointBuffer.count != PointCount)
            {
                ReleaseComputeBuffer(ref pointBuffer);
                pointBuffer = new ComputeBuffer(PointCount, sizeof(float) * 3 * 2);

                gravitationalField.SetInt   ("width",  width  + 1);
                gravitationalField.SetInt   ("height", height + 1);
                gravitationalField.SetInt   ("depth",  depth  + 1);
                gravitationalField.SetVector("offset", new Vector3(width, height, depth) * 0.5f);
                gravitationalField.SetBuffer(computePointPositionsKernel, "point_buffer", pointBuffer);
                gravitationalField.Dispatch(computePointPositionsKernel, ThreadsX, ThreadsY, ThreadsZ);

                pointsMaterial.SetBuffer("point_buffer", pointBuffer);
            }
        }

        private void ValidateGridBuffer()
        {
            if (gridBuffer == null || gridBuffer.count != PointCount)
            {
                ReleaseComputeBuffer(ref gridBuffer);
                gridBuffer = new ComputeBuffer(PointCount, sizeof(uint) * 3);

                gravitationalField.SetInt   ("width",  width  + 1);
                gravitationalField.SetInt   ("height", height + 1);
                gravitationalField.SetInt   ("depth",  depth  + 1);
                gravitationalField.SetBuffer(createGridKernel, "point_buffer", pointBuffer);
                gravitationalField.SetBuffer(createGridKernel, "grid_buffer",  gridBuffer);

                gridMaterial.SetBuffer("point_buffer", pointBuffer);
                gridMaterial.SetBuffer("grid_buffer",  gridBuffer);
            }
        }

        private void ValidateBodyBuffer()
        {
            if (bodies.Count > 0)
            {
                if (bodyBuffer == null || bodyBuffer.count != bodies.Count)
                {
                    ReleaseComputeBuffer(ref bodyBuffer);
                    bodyBuffer = new ComputeBuffer(bodies.Count, sizeof(float) * 4);// GravitationalBody.Data.Size);
                    gravitationalField.SetInt("body_count", bodies.Count);
                    gravitationalField.SetBuffer(computeDisplacementKernel, "point_buffer", pointBuffer);
                    gravitationalField.SetBuffer(computeDisplacementKernel, "body_buffer",  bodyBuffer);
                }

                if (bodyData == null || bodyData.Length != bodies.Count)
                {
                    //bodyData = new GravitationalBody.Data[bodies.Count];
                    bodyData = new Vector4[bodies.Count];
                }
            }
        }

        private void ValidateBeamBuffer()
        {
            if (beams.Count > 0)
            {
                if (beamBuffer == null || beamBuffer.count != beams.Count)
                {
                    ReleaseComputeBuffer(ref beamBuffer);
                    beamBuffer = new ComputeBuffer(beams.Count, sizeof(float) * 4);

                }
            }
        }

        private void ReleaseComputeBuffer(ref ComputeBuffer computeBuffer)
        {
            if (computeBuffer != null)
            {
                computeBuffer.Release();
                computeBuffer.Dispose();
                computeBuffer = null;
            }
        }
        #endregion

        #region ON DRAW GIZMOS
        private void OnDrawGizmos()
        {
            Color     gizmosColor  = Gizmos.color;
            Matrix4x4 gizmosMatrix = Gizmos.matrix;

            Gizmos.color  = new Color(1, 1, 1, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.DrawWireCube(Vector3.zero, new Vector3(width, height, depth));

            Gizmos.matrix = gizmosMatrix;
            Gizmos.color  = gizmosColor;
        }
        #endregion
    }
}