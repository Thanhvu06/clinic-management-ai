import { useEffect, useState } from 'react';
import { Skeleton } from 'antd';
import { Link } from 'react-router-dom';
import axiosClient from '../../../api/axiosClient';
import type { ApiResponse } from '../../../types';
import { formatDoctorName } from '../../../utils/doctorNameHelper';
import { slotLabel, slotRange, slotTimestamp } from './slotHelpers';
import type { DoctorSlot, LandingDoctor, LandingSlot } from './landingTypes';
import styles from './EarliestSlots.module.css';

const VISIBLE_SLOTS = 4;

export function EarliestSlots({ doctors, loading }: { doctors: LandingDoctor[]; loading: boolean }) {
    const [result, setResult] = useState<{ doctors: LandingDoctor[]; items: DoctorSlot[] }>();

    useEffect(() => {
        if (loading) return;
        let active = true;
        const range = slotRange();
        void Promise.allSettled(doctors.slice(0, 6).map(async doctor => {
            const response = await axiosClient.get<unknown, ApiResponse<LandingSlot[]>>(`/doctors/${doctor.id}/available-slots`, { params: range });
            const slot = (response?.success && Array.isArray(response.data) ? response.data : [])
                .filter(item => item.slotDate >= range.fromDate && item.slotDate <= range.toDate && slotTimestamp(item) > Date.now())
                .sort((a, b) => slotTimestamp(a) - slotTimestamp(b))[0];
            return slot ? { doctor, slot } : undefined;
        })).then(results => {
            if (!active) return;
            const items = results.flatMap(item => item.status === 'fulfilled' && item.value ? [item.value] : []);
            items.sort((a, b) => slotTimestamp(a.slot) - slotTimestamp(b.slot));
            setResult({ doctors, items });
        });
        return () => { active = false; };
    }, [doctors, loading]);

    const busy = loading || result?.doctors !== doctors;
    return (
        <aside className={styles.panel} aria-labelledby="earliest-slots-title" aria-busy={busy}>
            <div className={styles.heading}>
                <h2 id="earliest-slots-title">Lịch khám còn trống sớm nhất</h2>
                <p>Trong 7 ngày tới</p>
            </div>
            {busy ? <div className={styles.skeleton} role="status" aria-label="Đang tải lịch trống"><Skeleton active paragraph={{ rows: 4 }} title={false} /></div>
                : result?.items.length ? <><ul className={styles.slots}>{result.items.slice(0, VISIBLE_SLOTS).map(({ doctor, slot }) => {
                    const params = new URLSearchParams({ doctorId: String(doctor.id), date: slot.slotDate });
                    if (doctor.specialtyId) params.set('specialtyId', String(doctor.specialtyId));
                    return <li key={doctor.id} className={styles.slot}>
                        <div><strong>{formatDoctorName(doctor.academicTitle, doctor.fullName)}</strong>
                            {doctor.specialtyName && <span>{doctor.specialtyName}</span>}
                            <time dateTime={`${slot.slotDate}T${slot.startTime}+07:00`}>{slotLabel(slot)}</time>
                        </div>
                        <Link to={`/patient/book?${params}`}>Đặt giờ này</Link>
                    </li>;
                })}</ul>
                {result.items.length > VISIBLE_SLOTS && <Link className={styles.more} to="/patient/book">Xem thêm lịch trống</Link>}</> : <div className={styles.empty}>
                    <p>Hiện chưa có lịch trống trong 7 ngày tới</p>
                    <Link to="/patient/book">Đặt lịch khám</Link>
                </div>}
        </aside>
    );
}
