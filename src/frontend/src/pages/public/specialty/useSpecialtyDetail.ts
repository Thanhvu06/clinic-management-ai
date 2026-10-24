import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams } from 'react-router-dom';
import axiosClient from '../../../api/axiosClient';
import { toLocalDateString } from '../../../utils/formatters';
import { getApiErrorMessage } from '../../../utils/apiError';
import type { Specialty, Doctor, RecommendedDoctor, ApiResponse } from './specialtyTypes';

export function useSpecialtyDetail() {
    const { id } = useParams<{ id: string }>();
    const [specialty, setSpecialty] = useState<Specialty | null>(null);
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [recommendations, setRecommendations] = useState<RecommendedDoctor[]>([]);
    const [recommendationsLoading, setRecommendationsLoading] = useState(true);
    const [recommendationsError, setRecommendationsError] = useState<string | null>(null);
    const pageRequest = useRef<AbortController | null>(null);
    const recommendationsRequest = useRef<AbortController | null>(null);

    const fetchSpecialtyData = useCallback(async () => {
        pageRequest.current?.abort();
        const controller = new AbortController();
        pageRequest.current = controller;
        setLoading(true);
        setError(null);
        setSpecialty(null);
        setDoctors([]);
        try {
            if (!id) return;
            const [specRes, docRes] = await Promise.all([
                axiosClient.get<unknown, ApiResponse<Specialty>>(`/specialties/${id}`, { signal: controller.signal }),
                axiosClient.get<unknown, ApiResponse<{ items: Doctor[] } | Doctor[]>>(
                    `/specialties/${id}/doctors?page=1&pageSize=20`, { signal: controller.signal },
                ).catch(() => ({ success: false, data: [] as Doctor[] })),
            ]);
            if (controller.signal.aborted) return;
            if (specRes.success && specRes.data) setSpecialty(specRes.data);
            if (docRes.success && docRes.data) setDoctors(Array.isArray(docRes.data) ? docRes.data : docRes.data.items || []);
        } catch (err: unknown) {
            if (controller.signal.aborted) return;
            if (!(err && typeof err === 'object' && 'status' in err && err.status === 404)) {
                setError(getApiErrorMessage(err, 'Không thể tải thông tin chuyên khoa.'));
            }
        } finally {
            if (!controller.signal.aborted) setLoading(false);
        }
    }, [id]);

    const fetchRecommendations = useCallback(async () => {
        recommendationsRequest.current?.abort();
        const controller = new AbortController();
        recommendationsRequest.current = controller;
        setRecommendationsLoading(true);
        setRecommendationsError(null);
        setRecommendations([]);
        try {
            if (!id) return;
            const response = await axiosClient.get<unknown, ApiResponse<RecommendedDoctor[]>>(
                `/specialties/${id}/recommended-doctors`,
                { params: { fromDate: toLocalDateString(), days: 14 }, signal: controller.signal },
            );
            if (controller.signal.aborted) return;
            if (!response.success) {
                setRecommendationsError(response.message || 'Không thể tải bác sĩ được gợi ý.');
                return;
            }
            setRecommendations(response.data.slice(0, 3));
        } catch (err: unknown) {
            if (!controller.signal.aborted) setRecommendationsError(getApiErrorMessage(err, 'Không thể tải bác sĩ được gợi ý.'));
        } finally {
            if (!controller.signal.aborted) setRecommendationsLoading(false);
        }
    }, [id]);

    useEffect(() => {
        let active = true;
        // Start both requests after the route effect; discard a superseded route's work.
        void Promise.resolve().then(() => {
            if (!active) return;
            void fetchSpecialtyData();
            void fetchRecommendations();
        });
        return () => {
            active = false;
            pageRequest.current?.abort();
            recommendationsRequest.current?.abort();
        };
    }, [fetchSpecialtyData, fetchRecommendations]);

    return { specialty, doctors, loading, error, recommendations, recommendationsLoading, recommendationsError, fetchSpecialtyData, fetchRecommendations };
}
