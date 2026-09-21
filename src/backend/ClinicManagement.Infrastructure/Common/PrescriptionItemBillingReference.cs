namespace ClinicManagement.Infrastructure.Common;

/// <summary>
/// Provides safe bit-shift encoding and decoding for PrescriptionItem references in InvoiceItem.ReferenceId.
/// Prevents integer overflow and collisions for large Medicine IDs while maintaining backward compatibility
/// with legacy decimal encoding (prescriptionId * 100000L + medicineId).
/// </summary>
public static class PrescriptionItemBillingReference
{
    private const long LegacyMultiplier = 100000L;

    /// <summary>
    /// Encodes prescriptionId (upper 32 bits) and medicineId (lower 32 bits) into a 64-bit long.
    /// </summary>
    public static long Encode(long prescriptionId, long medicineId)
    {
        return (prescriptionId << 32) | (medicineId & 0xFFFFFFFFL);
    }

    /// <summary>
    /// Checks whether a given ReferenceId belongs to the specified prescription (either modern or legacy encoding).
    /// </summary>
    public static bool MatchesPrescription(long referenceId, long prescriptionId)
    {
        if ((referenceId >> 32) == prescriptionId)
            return true;

        if (referenceId >= prescriptionId * LegacyMultiplier && referenceId < (prescriptionId + 1) * LegacyMultiplier)
            return true;

        return referenceId == prescriptionId;
    }

    /// <summary>
    /// Tries to decode the medicineId from a ReferenceId for a given prescription.
    /// Supports both modern bit-shifted encoding and legacy decimal encoding.
    /// </summary>
    public static bool TryDecodeMedicineId(long referenceId, long prescriptionId, out long medicineId)
    {
        if ((referenceId >> 32) == prescriptionId)
        {
            medicineId = referenceId & 0xFFFFFFFFL;
            return true;
        }

        if (referenceId >= prescriptionId * LegacyMultiplier && referenceId < (prescriptionId + 1) * LegacyMultiplier)
        {
            medicineId = referenceId - (prescriptionId * LegacyMultiplier);
            return true;
        }

        medicineId = 0;
        return false;
    }
}
