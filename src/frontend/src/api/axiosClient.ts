import axios from 'axios';

declare module 'axios' {
    interface AxiosRequestConfig {
        suppressForbiddenRedirect?: boolean;
    }
}

const apiBase = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5258';

const axiosClient = axios.create({
    baseURL: `${apiBase.replace(/\/$/, '')}/api/v1`,
    headers: {
        'Content-Type': 'application/json',
    },
});

axiosClient.interceptors.request.use((config) => {
    const token = localStorage.getItem('token');
    if (token) {
        config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
});

axiosClient.interceptors.response.use(
    (response) => {
        return response.data; // The ApiResponse<T> object
    },
    (error) => {
        const payload = error.response?.data;
        if (error.response?.status === 401) {
            localStorage.removeItem('token');
            if (window.location.pathname !== '/login' && window.location.pathname !== '/register') {
                const currentFull = window.location.pathname + window.location.search + window.location.hash;
                const safeParam = currentFull && currentFull !== '/' ? `?returnUrl=${encodeURIComponent(currentFull)}` : '';
                window.location.href = `/login${safeParam}`;
            }
        } else if (error.response?.status === 403 && payload?.errorCode === 'FORBIDDEN' && !error.config?.suppressForbiddenRedirect) {
            if (window.location.pathname !== '/forbidden' && window.location.pathname !== '/403') {
                window.location.href = '/forbidden';
            }
        }
        if (error.response?.status === 429 && !payload?.message) {
            return Promise.reject({
                ...(payload && typeof payload === 'object' ? payload : {}),
                message: 'Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.',
                status: error.response.status,
                code: error.code
            });
        }
        if (payload && typeof payload === 'object') {
            return Promise.reject({
                ...(payload as Record<string, unknown>),
                status: error.response?.status,
                code: error.code
            });
        }
        return Promise.reject({ message: error.message, status: error.response?.status, code: error.code });
    }
);

export default axiosClient;
