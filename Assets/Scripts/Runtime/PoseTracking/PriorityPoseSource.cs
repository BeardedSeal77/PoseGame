using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PriorityPoseSource : PoseSourceBase
{
    [SerializeField] private List<PoseSourceBase> sources = new List<PoseSourceBase>();

    private PoseSourceBase activeSource;

    public override float MinimumConfidence => activeSource != null
        ? activeSource.MinimumConfidence
        : base.MinimumConfidence;

    public PoseSourceBase ActiveSource => activeSource;

    public void Configure(params PoseSourceBase[] orderedSources)
    {
        sources.Clear();
        if (orderedSources == null)
        {
            return;
        }

        for (int i = 0; i < orderedSources.Length; i++)
        {
            PoseSourceBase source = orderedSources[i];
            if (source != null && source != this)
            {
                sources.Add(source);
            }
        }
    }

    public override bool TryGetPose(PoseFrame outputFrame)
    {
        activeSource = null;

        if (outputFrame == null)
        {
            return false;
        }

        for (int i = 0; i < sources.Count; i++)
        {
            PoseSourceBase source = sources[i];
            if (source == null || source == this)
            {
                continue;
            }

            if (!source.isActiveAndEnabled)
            {
                continue;
            }

            if (!source.TryGetPose(outputFrame))
            {
                continue;
            }

            activeSource = source;
            return true;
        }

        return false;
    }
}