import type { PublicMedicineDto } from '../../../types';

export function medicineSections(medicine: PublicMedicineDto) {
    const fields = [
        ['description', 'Mô tả', medicine.description],
        ['indications', 'Chỉ định', medicine.indications],
        ['dosage', 'Liều dùng và cách dùng', medicine.dosageAndAdministration],
        ['contraindications', 'Chống chỉ định', medicine.contraindications],
        ['sideEffects', 'Tác dụng không mong muốn', medicine.sideEffects],
        ['precautions', 'Thận trọng', medicine.precautions],
        ['overdose', 'Xử trí quá liều', medicine.overdoseManagement],
        ['interactions', 'Tương tác thuốc', medicine.interactions?.length ? 'interactions' : null],
        ['storage', 'Bảo quản', medicine.storageInstructions],
    ];
    return fields.filter((field): field is [string, string, string] => Boolean(field[2]?.trim()))
        .map(([id, title, text]) => ({ id, title, text }));
}
