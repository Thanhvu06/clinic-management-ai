export interface RevisitBookingSeed {
    id: number;
    specialtyId: number;
    doctorId: number;
    suggestedDate: string;
}

export const buildRevisitBookingUrl = (request: RevisitBookingSeed): string | null => {
    if (
        !Number.isSafeInteger(request.id) ||
        request.id <= 0 ||
        !Number.isSafeInteger(request.specialtyId) ||
        request.specialtyId <= 0 ||
        !Number.isSafeInteger(request.doctorId) ||
        request.doctorId <= 0
    ) {
        return null;
    }

    const date = request.suggestedDate?.split("T")[0];
    if (!date || !/^\d{4}-\d{2}-\d{2}$/.test(date)) {
        return null;
    }

    const params = new URLSearchParams({
        revisitRequestId: request.id.toString(),
        specialtyId: request.specialtyId.toString(),
        doctorId: request.doctorId.toString(),
        date
    });

    return `/patient/book?${params.toString()}`;
};
