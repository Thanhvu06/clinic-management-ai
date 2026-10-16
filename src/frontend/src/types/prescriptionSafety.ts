export interface PrescriptionSafetyAllergyDto {
    id: number;
    patientId: number;
    allergenType: number;
    allergenTypeName: string;
    allergenName: string;
    severity: number;
    severityName: string;
    reactionDescription?: string | null;
    recordedAtUtc: string;
}

export interface PrescriptionSafetyWarningDto {
    level: 'Info' | 'Warning' | 'Danger';
    type: 'Allergy' | 'CrossAllergy' | 'Interaction' | 'DuplicateIngredient' | 'DuplicateClass' | 'MissingIngredient';
    medicineIds: number[];
    title: string;
    detail: string;
}

export interface PrescriptionSafetyResultDto {
    allergies: PrescriptionSafetyAllergyDto[];
    warnings: PrescriptionSafetyWarningDto[];
}
