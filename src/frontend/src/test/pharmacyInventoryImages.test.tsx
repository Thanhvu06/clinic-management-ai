import { expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import axiosClient from '../api/axiosClient';
import { DialogProvider } from '../contexts/DialogContext';
import { PharmacyInventory } from '../pages/pharmacy/PharmacyInventory';
import type { ActiveMedicineDto } from '../types';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));

it('renders active medicine images and low-stock or in-stock tags using reorderLevel', async () => {
    const medicines: ActiveMedicineDto[] = [
        { id: 1, code: 'IMG-LOW', name: 'Thuốc có ảnh', unit: 'Viên', stockQuantity: 5, reorderLevel: 5,
            imageUrl: '/media/medicines/medicine-photo.png', unitPrice: 1500, isPrescriptionRequired: true },
        { id: 2, code: 'IMG-HIGH', name: 'Thuốc còn hàng', unit: 'Viên', stockQuantity: 6, reorderLevel: 5,
            imageUrl: null, unitPrice: 2000, isPrescriptionRequired: false },
    ];
    vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data:
        String(url) === '/medicines/active' ? medicines : { items: [], totalItems: 0 },
    }));

    render(<DialogProvider><PharmacyInventory /></DialogProvider>);

    const grid = screen.getByRole('region', { name: 'Lưới thuốc trong kho' });
    const image = await within(grid).findByRole('img', { name: 'Thuốc có ảnh' });
    expect(image).toHaveAttribute('src', 'http://localhost:5258/media/medicines/medicine-photo.png');
    const lowStockCard = within(image.closest('.ant-card') as HTMLElement);
    expect(lowStockCard.getByText('Sắp hết')).toBeInTheDocument();
    expect(lowStockCard.queryByText('Còn hàng')).not.toBeInTheDocument();
    const inStockCard = within(within(grid).getByText('Thuốc còn hàng').closest('.ant-card') as HTMLElement);
    expect(inStockCard.getByText('Còn hàng')).toBeInTheDocument();
    expect(inStockCard.queryByText('Sắp hết')).not.toBeInTheDocument();
});
