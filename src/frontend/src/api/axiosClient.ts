import axios from 'axios';

const axiosClient = axios.create({
    baseURL: import.meta.env.VITE_API_BASE_URL + '/api/v1',
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
        return Promise.reject(error.response?.data || error.message);
    }
);

export default axiosClient;
