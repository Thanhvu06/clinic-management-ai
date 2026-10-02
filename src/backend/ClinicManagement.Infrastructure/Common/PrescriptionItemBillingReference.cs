namespace ClinicManagement.Infrastructure.Common;

/// <summary>
/// Provides safe bit-shift encoding and decoding for PrescriptionItem references in InvoiceItem.ReferenceId.
/// Uses explicit versioning (ReferenceType = "PrescriptionItem:v2" vs "PrescriptionItem") to prevent
/// collisions between modern bit-shifted encoding and legacy decimal encoding (prescriptionId * 100000L + medicineId).
/// </summary>
public static class PrescriptionItemBillingReference
{
    public const string ModernReferenceType = "PrescriptionItem:v2";
    public const string LegacyReferenceType = "PrescriptionItem";
    private const long LegacyMultiplier = 100000L;

    /// <summary>
    /// Encodes prescriptionId (upper 32 bits) and medicineId (lower 32 bits) into a 64-bit long.
    /// </summary>
    public static long Encode(long prescriptionId, long medicineId)
    {
        return (prescriptionId << 32) | (medicineId & 0xFFFFFFFFL);
    }

    /// <summary>
    /// Checks whether a given ReferenceId with a specified ReferenceType belongs to the prescription.
    /// Eliminates collisions by enforcing version-specific decoding.
    /// </summary>
    public static bool MatchesPrescription(string referenceType, long referenceId, long prescriptionId)
    {
        if (referenceType == ModernReferenceType)
        {
            return (referenceId >> 32) == prescriptionId;
        }

        if (referenceType == LegacyReferenceType)
        {
            if (referenceId == prescriptionId)
                return true;

            return referenceId >= prescriptionId * LegacyMultiplier && referenceId < (prescriptionId + 1) * LegacyMultiplier;
        }

        return false;
    }

    /// <summary>
    /// Overload for checking prescription matching when referenceType is not specified (backward compatibility).
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
    /// Tries to decode the medicineId from a ReferenceId for a given prescription, taking into account the ReferenceType.
    /// Prevents cross-prescription collision between modern bit-shifted and legacy decimal formats.
    /// </summary>
    public static bool TryDecodeMedicineId(string referenceType, long referenceId, long prescriptionId, out long medicineId)
    {
        if (referenceType == ModernReferenceType)
        {
            if ((referenceId >> 32) == prescriptionId)
            {
                medicineId = referenceId & 0xFFFFFFFFL;
                return true;
            }

            medicineId = 0;
            return false;
        }

        if (referenceType == LegacyReferenceType)
        {
            if (referenceId >= prescriptionId * LegacyMultiplier && referenceId < (prescriptionId + 1) * LegacyMultiplier)
            {
                medicineId = referenceId - (prescriptionId * LegacyMultiplier);
                return true;
            }

            medicineId = 0;
            return false;
        }

        medicineId = 0;
        return false;
    }

    /// <summary>
    /// Fallback overload for decoding without explicit ReferenceType.
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
