import { beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { diagnosticApi } from '../api/diagnosticApi';

vi.mock('../api/axiosClient', () => ({
    default: { get: vi.fn(), put: vi.fn() }
}));

describe('diagnostic service pricing API', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('updates a service price using the id before the pricing segment', async () => {
        await diagnosticApi.updateDiagnosticPrice(5, 120000);
        expect(axiosClient.put).toHaveBeenCalledWith('/admin/diagnostic-services/5/pricing', { price: 120000 });
    });

    it('keeps the pricing catalog GET endpoint', async () => {
        await diagnosticApi.getDiagnosticPricing();
        expect(axiosClient.get).toHaveBeenCalledWith('/admin/diagnostic-services/pricing');
    });
});
