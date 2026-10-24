import { Baby, Bone, Brain, Ear, Eye, HeartPulse, ScanFace, Sparkles, Stethoscope, Utensils, Venus, Wind } from 'lucide-react';

const icons = [
    [['tim mạch'], HeartPulse], [['mắt', 'nhãn'], Eye], [['nhi'], Baby],
    [['thần kinh'], Brain], [['xương', 'khớp'], Bone], [['tai', 'mũi', 'họng'], Ear],
    [['hô hấp'], Wind], [['răng'], ScanFace], [['da liễu'], Sparkles],
    [['tiêu hóa'], Utensils], [['sản', 'phụ khoa'], Venus],
] as const;

export function specialtyIcon(name: string) {
    const normalized = name.toLocaleLowerCase('vi-VN');
    return icons.find(([keywords]) => keywords.some(keyword => normalized.includes(keyword)))?.[1] ?? Stethoscope;
}
