export type MedicineBatchStatus = 'Valid' | 'Expiring' | 'Expired' | 'Disposed';
export type MedicineBatchFilter = 'all' | 'valid' | 'expiring' | 'expired';

export interface MedicineBatch {
    id: number;
    medicineId: number;
    medicineCode: string;
    medicineName: string;
    unit: string;
    batchNumber: string;
    expiryDate: string;
    quantityReceived: number;
    quantityRemaining: number;
    daysToExpiry: number;
    status: MedicineBatchStatus;
}

export interface CreateMedicineBatch {
    medicineId: number;
    batchNumber: string;
    expiryDate: string;
    quantity: number;
    unitCost?: number | null;
}

export interface MedicineBatchPage {
    items: MedicineBatch[];
    totalItems: number;
    page: number;
    pageSize: number;
}

export interface BatchAllocation {
    batchNumber: string;
    expiryDate: string;
    quantity: number;
}
