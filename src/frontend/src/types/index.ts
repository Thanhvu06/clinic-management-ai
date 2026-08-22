export interface ApiResponse<T = any> {
    success: boolean;
    message: string;
    data?: T;
}

export interface ApiErrorResponse {
    success: boolean;
    message: string;
    errorCode: string;
    errors?: Record<string, string[]>;
}

export interface UserDto {
    id?: number;
    userId: string;
    fullName: string;
    role: string;
}

export interface AuthResponse {
    accessToken: string;
    expiresAt: string;
    user: UserDto;
}
