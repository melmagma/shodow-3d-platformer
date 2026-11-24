using System.Collections.Generic;
using UnityEngine;

public class LightDetector : MonoBehaviour
{
    [SerializeField] private Transform targetObject;
    [SerializeField] private LayerMask occlusionLayers;

    [Header("Performance")]
    [SerializeField, Min(0.01f)] private float updateInterval = 0.1f;
    [SerializeField] private float directionalRayLength = 100f;

    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

    public float CurrentIntensity { get; private set; }

    
    private struct LightData
    {
        public Light light;
        public Transform tf;
        public LightType type;
        public float range;
        public float rangeSqr;
        public float cosOuter;
        public float invCosRange; 
    }

    private readonly List<LightData> lights = new List<LightData>();
    private float nextUpdateTime;

    private void Awake()
    {
        RefreshLights();
    }

    
    public void RefreshLights()
    {
        lights.Clear();
        foreach (Light l in FindObjectsByType<Light>())
        {
            if (l.type != LightType.Directional &&
                l.type != LightType.Point &&
                l.type != LightType.Spot) continue;

            var data = new LightData
            {
                light = l,
                tf = l.transform,
                type = l.type,
                range = l.range,
                rangeSqr = l.range * l.range
            };

            if (l.type == LightType.Spot)
            {
                float cosOuter = Mathf.Cos(l.spotAngle * 0.5f * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(l.innerSpotAngle * 0.5f * Mathf.Deg2Rad);
                data.cosOuter = cosOuter;
                data.invCosRange = 1f / Mathf.Max(cosInner - cosOuter, 0.0001f);
            }

            lights.Add(data);
        }
    }

    private void Update()
    {
        if (targetObject == null || Time.time < nextUpdateTime) return;
        nextUpdateTime = Time.time + updateInterval;

        CurrentIntensity = GetDirectLightIntensityOnObject(targetObject);

        if (logToConsole)
            Debug.Log($"[Light Detector] {targetObject.name} light intensity: {CurrentIntensity:F2}");
    }

    public float GetDirectLightIntensityOnObject(Transform target)
    {
        float total = 0f;
        Vector3 targetPos = target.position;

        for (int i = 0; i < lights.Count; i++)
        {
            LightData d = lights[i];
            Light light = d.light;

            if (light == null || !light.isActiveAndEnabled) continue;
            float lightIntensity = light.intensity;
            if (lightIntensity <= 0f) continue;

            Vector3 rayStart, rayDir;
            float rayDist;
            float attenuation = 1f;

            if (d.type == LightType.Directional)
            {
                Vector3 toLightDir = -d.tf.forward;
                rayStart = targetPos + toLightDir * directionalRayLength;
                rayDir = -toLightDir;
                rayDist = directionalRayLength;
            }
            else
            {
                Vector3 lightPos = d.tf.position;
                Vector3 toLight = lightPos - targetPos;
                float sqrDist = toLight.sqrMagnitude;

                
                if (sqrDist > d.rangeSqr) continue;

                if (sqrDist < 0.0001f)
                {
                    total += lightIntensity; 
                    continue;
                }

                float dist = Mathf.Sqrt(sqrDist);
                Vector3 lightToTarget = -toLight / dist;

                attenuation = 1f - dist / d.range;
                attenuation *= attenuation;

                if (d.type == LightType.Spot)
                {
                    float cosAngle = Vector3.Dot(d.tf.forward, lightToTarget);
                    if (cosAngle < d.cosOuter) continue;
                    attenuation *= Mathf.Clamp01((cosAngle - d.cosOuter) * d.invCosRange);
                }

                rayStart = lightPos;
                rayDir = lightToTarget;
                rayDist = dist;
            }

            
            if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, rayDist,
                                occlusionLayers, QueryTriggerInteraction.Ignore))
            {
                Transform t = hit.transform;
                if (t != target && !t.IsChildOf(target)) continue; 
            }

            total += lightIntensity * attenuation;
        }

        return total;
    }
}
