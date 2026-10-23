export interface MedicineCatalogFields {
    activeIngredient?: string | null;
    strength?: string | null;
    dosageForm?: string | null;
    manufacturer?: string | null;
    categoryId?: number | null;
    isPrescriptionRequired: boolean;
    description?: string | null;
    storageInstructions?: string | null;
    indications?: string | null;
    dosageAndAdministration?: string | null;
    contraindications?: string | null;
    sideEffects?: string | null;
    precautions?: string | null;
    overdoseManagement?: string | null;
    imagePath?: string | null;
}
export interface MedicineDto extends MedicineCatalogFields {
    usableStock?: number;
    id: number;
    code: string;
    name: string;
    unit: string;
    stockQuantity: number;
    reorderLevel: number;
    unitPrice?: number | null;
    isActive: boolean;
    createdAt: string;
    updatedAt?: string | null;
    categoryName?: string | null;
    imageUrl?: string | null;
}
export interface ActiveMedicineDto {
    usableStock?: number;
    id: number;
    code: string;
    name: string;
    unit: string;
    stockQuantity: number;
    imageUrl?: string | null;
    reorderLevel: number;
    unitPrice?: number | null;
    isPrescriptionRequired: boolean;
    strength?: string | null;
}
export interface MedicineCategoryDto {
    id: number;
    name: string;
    description?: string | null;
    sortOrder: number;
    isActive: boolean;
    createdAt: string;
    updatedAt?: string | null;
}
export type SaveMedicineCategoryDto = Pick<MedicineCategoryDto, 'name' | 'description' | 'sortOrder'>;
export type PublicMedicineCategoryDto = Pick<MedicineCategoryDto, 'id' | 'name' | 'description' | 'sortOrder'>;
export interface PublicMedicineDto {
    id: number;
    code: string;
    name: string;
    activeIngredient?: string | null;
    strength?: string | null;
    dosageForm?: string | null;
    unit: string;
    categoryName?: string | null;
    isPrescriptionRequired: boolean;
    unitPrice?: number | null;
    imageUrl?: string | null;
    description?: string | null;
    storageInstructions?: string | null;
    indications?: string | null;
    dosageAndAdministration?: string | null;
    contraindications?: string | null;
    sideEffects?: string | null;
    precautions?: string | null;
    overdoseManagement?: string | null;
    interactions?: PublicMedicineInteractionDto[];
    availability: 'out_of_stock' | 'low' | 'in_stock';
}
export interface PublicMedicineInteractionDto {
    otherIngredient: string;
    severity: 'Contraindicated' | 'Major' | 'Moderate' | 'Minor';
    description: string;
    recommendation: string;
}
export interface MedicinePage<T> { items: T[]; page: number; pageSize: number; totalItems: number; totalPages: number }
