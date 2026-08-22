import React, { createContext, useContext, useState, useEffect } from 'react';
import axiosClient from '../api/axiosClient';
import type { UserDto, ApiResponse } from '../types';

interface AuthContextType {
    user: UserDto | null;
    isAuthenticated: boolean;
    loading: boolean;
    login: (token: string, user: UserDto) => void;
    logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const [user, setUser] = useState<UserDto | null>(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        const initAuth = async () => {
            const token = localStorage.getItem('token');
            if (token) {
                try {
                    const res = await axiosClient.get<any, ApiResponse<UserDto>>('/auth/me');
                    if (res.success && res.data) {
                        setUser(res.data);
                    } else {
                        localStorage.removeItem('token');
                    }
                } catch (error) {
                    localStorage.removeItem('token');
                }
            }
            setLoading(false);
        };
        initAuth();
    }, []);

    const login = (token: string, user: UserDto) => {
        localStorage.setItem('token', token);
        setUser(user);
    };

    const logout = async () => {
        try {
            await axiosClient.post('/auth/logout');
        } catch (error) {
            // Ignore logout errors
        } finally {
            localStorage.removeItem('token');
            setUser(null);
        }
    };

    return (
        <AuthContext.Provider value={{ user, isAuthenticated: !!user, loading, login, logout }}>
            {children}
        </AuthContext.Provider>
    );
};

export const useAuth = () => {
    const context = useContext(AuthContext);
    if (context === undefined) {
        throw new Error('useAuth must be used within an AuthProvider');
    }
    return context;
};
