import { beforeEach, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { diagnosticApi } from '../api/diagnosticApi';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
beforeEach(() => vi.clearAllMocks());

it('loads the verification queue through the endpoint authorized for both lab roles', async () => {
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: { items: [], totalItems: 0 } });
    await diagnosticApi.getVerificationQueue({ page: 2, pageSize: 15 });
    expect(axiosClient.get).toHaveBeenCalledWith('/diagnostic-verification/queue', { params: { page: 2, pageSize: 15 } });
});
