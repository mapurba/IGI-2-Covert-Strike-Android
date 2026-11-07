using Silk.NET.Vulkan;

namespace ProjectIGI.Rendering.Vulkan.Sync;

/// <summary>
/// Collects the barriers the traced world's builds need and issues them as one dependency.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline barrier is a synchronisation point for the whole GPU, not for the resource it names: two
/// barriers issued back to back drain the pipeline twice for what could have been one stall. The builds of
/// a frame's acceleration structures are ordered by memory barriers alone -- their output is reachable
/// through device addresses rather than through a bound resource, so there is no per-resource barrier to
/// write -- and every one wanted at the same point is merged into a single call.
/// </para>
/// <para>
/// Synchronisation 2, because it states the stage masks on the barrier itself rather than on the call.
/// Where the adapter has no synchronisation 2 -- a Vulkan 1.1 or 1.2 device without it -- the same
/// dependency is issued as the original <c>vkCmdPipelineBarrier</c>, with the stage and access masks
/// translated down. An acceleration-structure build has no stage in the original API, so it is ordered
/// against the compute stage, which is where its work runs.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanBarrierBatcher
{
    private readonly Vk _vk;
    private readonly bool _synchronization2;
    private MemoryBarrier2 _memoryBarrier;
    private bool _hasMemoryBarrier;

    /// <summary>Initialises a new instance of the <see cref="VulkanBarrierBatcher"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="synchronization2">
    /// Whether the adapter has synchronisation 2: when false, barriers are issued through the original
    /// <c>vkCmdPipelineBarrier</c> instead, because <c>vkCmdPipelineBarrier2</c> is a null function
    /// pointer on a device without it.
    /// </param>
    public VulkanBarrierBatcher(Vk vk, bool synchronization2)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;
        _synchronization2 = synchronization2;
    }

    /// <summary>Gets a value indicating whether anything is waiting to be flushed.</summary>
    public bool IsEmpty => !_hasMemoryBarrier;

    /// <summary>Adds a global memory barrier, merged with any already pending.</summary>
    /// <param name="srcStages">The stages that must finish.</param>
    /// <param name="srcAccess">The writes to make available.</param>
    /// <param name="dstStages">The stages that must wait.</param>
    /// <param name="dstAccess">The accesses to make visible.</param>
    /// <remarks>
    /// There is only ever one memory barrier in a batch: a second would stall the same stages for the
    /// same reason, so the masks are merged instead.
    /// </remarks>
    public void AddMemory(
        PipelineStageFlags2 srcStages,
        AccessFlags2 srcAccess,
        PipelineStageFlags2 dstStages,
        AccessFlags2 dstAccess)
    {
        if (_hasMemoryBarrier)
        {
            _memoryBarrier.SrcStageMask |= srcStages;
            _memoryBarrier.SrcAccessMask |= srcAccess;
            _memoryBarrier.DstStageMask |= dstStages;
            _memoryBarrier.DstAccessMask |= dstAccess;
            return;
        }

        _memoryBarrier = new MemoryBarrier2
        {
            SType = StructureType.MemoryBarrier2,
            SrcStageMask = srcStages,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStages,
            DstAccessMask = dstAccess,
        };
        _hasMemoryBarrier = true;
    }

    /// <summary>Orders a build after the traversals that were reading what it is about to overwrite.</summary>
    /// <remarks>
    /// The other direction to <see cref="AddAccelerationStructureBuild"/>, and the one that is easy to
    /// forget: a structure is rebuilt where it stands, so a build is a write into memory that rays were
    /// reading a moment ago -- and on this queue "a moment ago" includes a frame that has been submitted
    /// and is still running. The two sides are not symmetric: anything may traverse a structure -- the
    /// ray-query dispatches, and a top-level build reading the bottom-level structures under it -- but
    /// only the build stage may write one. No ray-tracing-pipeline stage: this backend traces from
    /// compute, and naming a stage the enabled features do not cover is itself an error.
    /// </remarks>
    public void AddAccelerationStructureReadToBuild() => AddMemory(
        PipelineStageFlags2.ComputeShaderBit | PipelineStageFlags2.AccelerationStructureBuildBitKhr,
        AccessFlags2.AccelerationStructureReadBitKhr,
        PipelineStageFlags2.AccelerationStructureBuildBitKhr,
        AccessFlags2.AccelerationStructureWriteBitKhr);

    /// <summary>Makes vertex and index data written by a copy or a compute pass visible to a build that reads it.</summary>
    public void AddGeometryWriteToBuild() => AddMemory(
        PipelineStageFlags2.CopyBit | PipelineStageFlags2.TransferBit | PipelineStageFlags2.ComputeShaderBit,
        AccessFlags2.TransferWriteBit | AccessFlags2.ShaderWriteBit,
        PipelineStageFlags2.AccelerationStructureBuildBitKhr,
        AccessFlags2.ShaderReadBit);

    /// <summary>Makes everything a build wrote visible to whatever traverses or builds on it next.</summary>
    /// <remarks>
    /// Deliberately coarse: a build that feeds another build -- bottom level into top level -- and a
    /// traversal that reads the result are all covered by one memory barrier over the build and compute
    /// stages.
    /// </remarks>
    public void AddAccelerationStructureBuild()
    {
        const PipelineStageFlags2 stages =
            PipelineStageFlags2.AccelerationStructureBuildBitKhr | PipelineStageFlags2.ComputeShaderBit;

        AddMemory(
            stages,
            AccessFlags2.AccelerationStructureWriteBitKhr,
            stages,
            AccessFlags2.AccelerationStructureReadBitKhr | AccessFlags2.AccelerationStructureWriteBitKhr);
    }

    /// <summary>Issues everything collected as one dependency, and empties the batch.</summary>
    /// <param name="commandBuffer">The command buffer to record into.</param>
    public void Flush(CommandBuffer commandBuffer)
    {
        if (IsEmpty)
        {
            return;
        }

        if (_synchronization2)
        {
            MemoryBarrier2 memory = _memoryBarrier;
            DependencyInfo info = new()
            {
                SType = StructureType.DependencyInfo,
                MemoryBarrierCount = 1,
                PMemoryBarriers = &memory,
            };

            _vk.CmdPipelineBarrier2(commandBuffer, &info);
        }
        else
        {
            // The same dependency in the form Vulkan 1.1 understands: one memory barrier with the
            // stage and access masks translated down. A wider mask is always safe, so anything the
            // translation does not know is folded into the widest stage and access rather than
            // dropped.
            MemoryBarrier barrier = new()
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = ToAccessFlags(_memoryBarrier.SrcAccessMask),
                DstAccessMask = ToAccessFlags(_memoryBarrier.DstAccessMask),
            };

            _vk.CmdPipelineBarrier(
                commandBuffer,
                ToStageFlags(_memoryBarrier.SrcStageMask),
                ToStageFlags(_memoryBarrier.DstStageMask),
                0,
                1,
                &barrier,
                0,
                null,
                0,
                null);
        }

        Reset();
    }

    /// <summary>Throws away everything collected without issuing it.</summary>
    public void Reset()
    {
        _hasMemoryBarrier = false;
        _memoryBarrier = default;
    }

    // Synchronisation 2's stage masks carry the same low bits as the original's for every stage the
    // original had, but the batcher's own stages are acceleration-structure builds, which the original
    // has no stage for: a build is ordered against the compute stage, which is where its work runs.
    // Anything left over is ordered against everything, which is wider than needed and always correct,
    // rather than dropped.
    private static PipelineStageFlags ToStageFlags(PipelineStageFlags2 stages)
    {
        PipelineStageFlags mapped = 0;

        if (stages.HasFlag(PipelineStageFlags2.TopOfPipeBit)) mapped |= PipelineStageFlags.TopOfPipeBit;
        if (stages.HasFlag(PipelineStageFlags2.DrawIndirectBit)) mapped |= PipelineStageFlags.DrawIndirectBit;
        if (stages.HasFlag(PipelineStageFlags2.VertexInputBit)) mapped |= PipelineStageFlags.VertexInputBit;
        if (stages.HasFlag(PipelineStageFlags2.VertexShaderBit)) mapped |= PipelineStageFlags.VertexShaderBit;
        if (stages.HasFlag(PipelineStageFlags2.TessellationControlShaderBit)) mapped |= PipelineStageFlags.TessellationControlShaderBit;
        if (stages.HasFlag(PipelineStageFlags2.TessellationEvaluationShaderBit)) mapped |= PipelineStageFlags.TessellationEvaluationShaderBit;
        if (stages.HasFlag(PipelineStageFlags2.GeometryShaderBit)) mapped |= PipelineStageFlags.GeometryShaderBit;
        if (stages.HasFlag(PipelineStageFlags2.FragmentShaderBit)) mapped |= PipelineStageFlags.FragmentShaderBit;
        if (stages.HasFlag(PipelineStageFlags2.EarlyFragmentTestsBit)) mapped |= PipelineStageFlags.EarlyFragmentTestsBit;
        if (stages.HasFlag(PipelineStageFlags2.LateFragmentTestsBit)) mapped |= PipelineStageFlags.LateFragmentTestsBit;
        if (stages.HasFlag(PipelineStageFlags2.ColorAttachmentOutputBit)) mapped |= PipelineStageFlags.ColorAttachmentOutputBit;
        if (stages.HasFlag(PipelineStageFlags2.ComputeShaderBit)) mapped |= PipelineStageFlags.ComputeShaderBit;
        if (stages.HasFlag(PipelineStageFlags2.TransferBit)) mapped |= PipelineStageFlags.TransferBit;
        if (stages.HasFlag(PipelineStageFlags2.BottomOfPipeBit)) mapped |= PipelineStageFlags.BottomOfPipeBit;
        if (stages.HasFlag(PipelineStageFlags2.HostBit)) mapped |= PipelineStageFlags.HostBit;
        if (stages.HasFlag(PipelineStageFlags2.AllGraphicsBit)) mapped |= PipelineStageFlags.AllGraphicsBit;
        if (stages.HasFlag(PipelineStageFlags2.AllCommandsBit)) mapped |= PipelineStageFlags.AllCommandsBit;

        // Synchronisation 2's aliases of the transfer stage, which the original spells one way.
        if (stages.HasFlag(PipelineStageFlags2.CopyBit)) mapped |= PipelineStageFlags.TransferBit;
        if (stages.HasFlag(PipelineStageFlags2.ResolveBit)) mapped |= PipelineStageFlags.TransferBit;
        if (stages.HasFlag(PipelineStageFlags2.BlitBit)) mapped |= PipelineStageFlags.TransferBit;
        if (stages.HasFlag(PipelineStageFlags2.ClearBit)) mapped |= PipelineStageFlags.TransferBit;

        // No original stage exists for an acceleration-structure build or a ray-tracing shader: both
        // run where compute runs, so that is the stage they are ordered against.
        if (stages.HasFlag(PipelineStageFlags2.AccelerationStructureBuildBitKhr)) mapped |= PipelineStageFlags.ComputeShaderBit;
        if (stages.HasFlag(PipelineStageFlags2.RayTracingShaderBitKhr)) mapped |= PipelineStageFlags.ComputeShaderBit;
        if (stages.HasFlag(PipelineStageFlags2.AccelerationStructureCopyBitKhr)) mapped |= PipelineStageFlags.TransferBit;

        PipelineStageFlags2 known =
            PipelineStageFlags2.TopOfPipeBit
            | PipelineStageFlags2.DrawIndirectBit
            | PipelineStageFlags2.VertexInputBit
            | PipelineStageFlags2.VertexShaderBit
            | PipelineStageFlags2.TessellationControlShaderBit
            | PipelineStageFlags2.TessellationEvaluationShaderBit
            | PipelineStageFlags2.GeometryShaderBit
            | PipelineStageFlags2.FragmentShaderBit
            | PipelineStageFlags2.EarlyFragmentTestsBit
            | PipelineStageFlags2.LateFragmentTestsBit
            | PipelineStageFlags2.ColorAttachmentOutputBit
            | PipelineStageFlags2.ComputeShaderBit
            | PipelineStageFlags2.TransferBit
            | PipelineStageFlags2.BottomOfPipeBit
            | PipelineStageFlags2.HostBit
            | PipelineStageFlags2.AllGraphicsBit
            | PipelineStageFlags2.AllCommandsBit
            | PipelineStageFlags2.CopyBit
            | PipelineStageFlags2.ResolveBit
            | PipelineStageFlags2.BlitBit
            | PipelineStageFlags2.ClearBit
            | PipelineStageFlags2.AccelerationStructureBuildBitKhr
            | PipelineStageFlags2.RayTracingShaderBitKhr
            | PipelineStageFlags2.AccelerationStructureCopyBitKhr;

        if ((stages & ~known) != 0)
        {
            mapped |= PipelineStageFlags.AllCommandsBit;
        }

        return mapped;
    }

    // The same translation for access masks. An acceleration structure is read and written through
    // shader accesses in the original API, which has no access bit of its own for one.
    private static AccessFlags ToAccessFlags(AccessFlags2 access)
    {
        AccessFlags mapped = 0;

        if (access.HasFlag(AccessFlags2.IndirectCommandReadBit)) mapped |= AccessFlags.IndirectCommandReadBit;
        if (access.HasFlag(AccessFlags2.IndexReadBit)) mapped |= AccessFlags.IndexReadBit;
        if (access.HasFlag(AccessFlags2.VertexAttributeReadBit)) mapped |= AccessFlags.VertexAttributeReadBit;
        if (access.HasFlag(AccessFlags2.UniformReadBit)) mapped |= AccessFlags.UniformReadBit;
        if (access.HasFlag(AccessFlags2.InputAttachmentReadBit)) mapped |= AccessFlags.InputAttachmentReadBit;
        if (access.HasFlag(AccessFlags2.ShaderReadBit)) mapped |= AccessFlags.ShaderReadBit;
        if (access.HasFlag(AccessFlags2.ShaderWriteBit)) mapped |= AccessFlags.ShaderWriteBit;
        if (access.HasFlag(AccessFlags2.ColorAttachmentReadBit)) mapped |= AccessFlags.ColorAttachmentReadBit;
        if (access.HasFlag(AccessFlags2.ColorAttachmentWriteBit)) mapped |= AccessFlags.ColorAttachmentWriteBit;
        if (access.HasFlag(AccessFlags2.DepthStencilAttachmentReadBit)) mapped |= AccessFlags.DepthStencilAttachmentReadBit;
        if (access.HasFlag(AccessFlags2.DepthStencilAttachmentWriteBit)) mapped |= AccessFlags.DepthStencilAttachmentWriteBit;
        if (access.HasFlag(AccessFlags2.TransferReadBit)) mapped |= AccessFlags.TransferReadBit;
        if (access.HasFlag(AccessFlags2.TransferWriteBit)) mapped |= AccessFlags.TransferWriteBit;
        if (access.HasFlag(AccessFlags2.HostReadBit)) mapped |= AccessFlags.HostReadBit;
        if (access.HasFlag(AccessFlags2.HostWriteBit)) mapped |= AccessFlags.HostWriteBit;
        if (access.HasFlag(AccessFlags2.MemoryReadBit)) mapped |= AccessFlags.MemoryReadBit;
        if (access.HasFlag(AccessFlags2.MemoryWriteBit)) mapped |= AccessFlags.MemoryWriteBit;

        if (access.HasFlag(AccessFlags2.AccelerationStructureReadBitKhr)) mapped |= AccessFlags.ShaderReadBit;
        if (access.HasFlag(AccessFlags2.AccelerationStructureWriteBitKhr)) mapped |= AccessFlags.ShaderWriteBit;
        if (access.HasFlag(AccessFlags2.ShaderSampledReadBit)) mapped |= AccessFlags.ShaderReadBit;
        if (access.HasFlag(AccessFlags2.ShaderStorageReadBit)) mapped |= AccessFlags.ShaderReadBit;
        if (access.HasFlag(AccessFlags2.ShaderStorageWriteBit)) mapped |= AccessFlags.ShaderWriteBit;

        AccessFlags2 known =
            AccessFlags2.IndirectCommandReadBit
            | AccessFlags2.IndexReadBit
            | AccessFlags2.VertexAttributeReadBit
            | AccessFlags2.UniformReadBit
            | AccessFlags2.InputAttachmentReadBit
            | AccessFlags2.ShaderReadBit
            | AccessFlags2.ShaderWriteBit
            | AccessFlags2.ColorAttachmentReadBit
            | AccessFlags2.ColorAttachmentWriteBit
            | AccessFlags2.DepthStencilAttachmentReadBit
            | AccessFlags2.DepthStencilAttachmentWriteBit
            | AccessFlags2.TransferReadBit
            | AccessFlags2.TransferWriteBit
            | AccessFlags2.HostReadBit
            | AccessFlags2.HostWriteBit
            | AccessFlags2.MemoryReadBit
            | AccessFlags2.MemoryWriteBit
            | AccessFlags2.AccelerationStructureReadBitKhr
            | AccessFlags2.AccelerationStructureWriteBitKhr
            | AccessFlags2.ShaderSampledReadBit
            | AccessFlags2.ShaderStorageReadBit
            | AccessFlags2.ShaderStorageWriteBit;

        if ((access & ~known) != 0)
        {
            mapped |= AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit;
        }

        return mapped;
    }
}
