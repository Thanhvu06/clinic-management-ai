const packageImages = [
    [['tổng quát'], 'goi-tong-quat'],
    [['tim mạch'], 'goi-tim-mach'],
    [['gan', 'chuyển hóa'], 'goi-gan-mat'],
    [['nhi'], 'goi-nhi-khoa'],
    [['xương khớp'], 'goi-co-xuong-khop'],
    [['phụ nữ'], 'goi-phu-nu'],
] as const;

export function packageImage(name: string, imageUrl?: string | null): string | undefined {
    if (imageUrl?.trim()) return imageUrl;
    const normalized = name.toLocaleLowerCase('vi-VN');
    const match = packageImages.find(([keywords]) => keywords.some(keyword => normalized.includes(keyword)));
    return match ? `/images/landing/${match[1]}.webp` : undefined;
}
