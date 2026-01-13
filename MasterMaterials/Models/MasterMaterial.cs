using System.Text.Json.Serialization;

namespace AssetProcessor.MasterMaterials.Models;

/// <summary>
/// Represents a Master Material configuration
/// </summary>
public class MasterMaterial
{
    /// <summary>
    /// Unique name for the master material
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description
    /// </summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// PlayCanvas material ID to use as base template
    /// </summary>
    [JsonPropertyName("baseMaterialId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BaseMaterialId { get; set; }

    /// <summary>
    /// List of chunk IDs used by this master material (legacy, for backwards compatibility)
    /// </summary>
    [JsonPropertyName("chunkIds")]
    public List<string> ChunkIds { get; set; } = [];

    /// <summary>
    /// Slot-based chunk assignments (new system)
    /// </summary>
    [JsonPropertyName("slotAssignments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChunkSlotAssignment>? SlotAssignments { get; set; }

    /// <summary>
    /// Default parameter values for instances
    /// </summary>
    [JsonPropertyName("defaultParams")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? DefaultParams { get; set; }

    /// <summary>
    /// Blend type: "opaque", "alpha", "additive", "premul"
    /// </summary>
    [JsonPropertyName("blendType")]
    public string BlendType { get; set; } = "opaque";

    /// <summary>
    /// Whether this is a built-in system master (pbr_opaque, etc.)
    /// </summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Gets the chunk ID for a specific slot, or default if not assigned
    /// </summary>
    public string GetChunkForSlot(string slotId)
    {
        var assignment = SlotAssignments?.FirstOrDefault(a => a.SlotId == slotId);
        if (assignment != null && !string.IsNullOrEmpty(assignment.ChunkId))
        {
            return assignment.ChunkId;
        }

        // Return default chunk for the slot
        var slot = ShaderChunkSchema.GetSlot(slotId);
        return slot?.DefaultChunkId ?? string.Empty;
    }

    /// <summary>
    /// Sets the chunk for a specific slot
    /// </summary>
    public void SetChunkForSlot(string slotId, string? chunkId)
    {
        SlotAssignments ??= [];

        var existing = SlotAssignments.FirstOrDefault(a => a.SlotId == slotId);
        if (existing != null)
        {
            existing.ChunkId = chunkId;
        }
        else
        {
            SlotAssignments.Add(new ChunkSlotAssignment
            {
                SlotId = slotId,
                ChunkId = chunkId,
                Enabled = true
            });
        }

        // Also update ChunkIds for backwards compatibility
        UpdateChunkIdsFromSlots();
    }

    /// <summary>
    /// Checks if a slot is enabled
    /// </summary>
    public bool IsSlotEnabled(string slotId)
    {
        var assignment = SlotAssignments?.FirstOrDefault(a => a.SlotId == slotId);
        if (assignment != null)
        {
            return assignment.Enabled;
        }

        // Check if slot is enabled by default
        var slot = ShaderChunkSchema.GetSlot(slotId);
        return slot?.IsEnabledByDefault ?? false;
    }

    /// <summary>
    /// Sets whether a slot is enabled
    /// </summary>
    public void SetSlotEnabled(string slotId, bool enabled)
    {
        SlotAssignments ??= [];

        var existing = SlotAssignments.FirstOrDefault(a => a.SlotId == slotId);
        if (existing != null)
        {
            existing.Enabled = enabled;
        }
        else
        {
            SlotAssignments.Add(new ChunkSlotAssignment
            {
                SlotId = slotId,
                ChunkId = null,
                Enabled = enabled
            });
        }

        UpdateChunkIdsFromSlots();
    }

    /// <summary>
    /// Updates ChunkIds list from slot assignments for backwards compatibility
    /// </summary>
    private void UpdateChunkIdsFromSlots()
    {
        if (SlotAssignments == null) return;

        ChunkIds = SlotAssignments
            .Where(a => a.Enabled && !string.IsNullOrEmpty(a.ChunkId))
            .Select(a => a.ChunkId!)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Initializes slot assignments from the schema defaults
    /// </summary>
    public void InitializeSlotAssignments()
    {
        SlotAssignments = ShaderChunkSchema.CreateDefaultAssignments();
    }
}
