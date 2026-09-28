import axios from 'axios';

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
        if (error.response?.status === 401) {
            localStorage.removeItem('token');
            if (window.location.pathname !== '/login' && window.location.pathname !== '/register') {
                const currentFull = window.location.pathname + window.location.search + window.location.hash;
                const safeParam = currentFull && currentFull !== '/' ? `?returnUrl=${encodeURIComponent(currentFull)}` : '';
                window.location.href = `/login${safeParam}`;
            }
        } else if (error.response?.status === 403) {
            if (window.location.pathname !== '/forbidden' && window.location.pathname !== '/403') {
                window.location.href = '/forbidden';
            }
        }
        const payload = error.response?.data;
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
