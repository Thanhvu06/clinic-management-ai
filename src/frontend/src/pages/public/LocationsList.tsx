import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import axiosClient from '../../api/axiosClient';
import type { ClinicLocationDto } from '../../types';
import styles from './PublicPages.module.css';
import { InlineError } from '../../components/common/InlineError';
import { LinkButton } from '../../components/common/LinkButton';
import { getApiErrorMessage } from '../../utils/apiError';

export const LocationsList: React.FC = () => {
    const [locations, setLocations] = useState<ClinicLocationDto[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [selectedCity, setSelectedCity] = useState<string>('all');

    const [reloadKey, setReloadKey] = useState(0);

    useEffect(() => {
        const fetchLocations = async () => {
            try {
                const res = await axiosClient.get<any, any>('/locations');
                if (res.success && res.data) {
                    setError(null);
                    setLocations(res.data);
                } else {
                    setError('Không thể tải danh sách cơ sở khám.');
                }
            } catch (err) {
                setError(getApiErrorMessage(err, 'Không thể tải danh sách cơ sở khám.'));
            } finally {
                setLoading(false);
            }
        };
        void fetchLocations();
    }, [reloadKey]);

    const uniqueCities = Array.from(new Set(locations.map(loc => loc.city))).filter(Boolean);
    const cities = [
        { id: 'all', name: 'Tất cả cơ sở' },
        ...uniqueCities.map(c => ({ id: c, name: c }))
    ];

    const filteredLocations = locations.filter(loc => {
        if (selectedCity === 'all') return true;
        return loc.city === selectedCity;
    });

    return (
        <div className={styles.pageContainer}>
            <div className={styles.pageHeader}>
                <nav className={styles.breadcrumb} aria-label="Đường dẫn trang">
                    <Link to="/">Trang chủ</Link>
                    <span>/</span>
                    <span aria-current="page">Cơ sở khám</span>
                </nav>
                <h1 className={styles.pageTitle}>Cơ sở khám</h1>
            </div>

            <div className={styles.toolbar}>
                <div className={styles.flexLayout12}>
                    {cities.map(c => (
                        <button
                            key={c.id}
                            type="button"
                            onClick={() => setSelectedCity(c.id)}
                            className={[styles.button2, (selectedCity === c.id ? styles.state10 : styles.state11), (selectedCity === c.id ? styles.state12 : styles.state4), (selectedCity === c.id ? styles.state13 : styles.state14)].join(' ')}
                        >
                            {c.name}
                        </button>
                    ))}
                </div>
                <div className={styles.resultCount}>
                    Hiển thị {filteredLocations.length} cơ sở
                </div>
            </div>

            {loading ? (
                <div className={styles.grid3}>
                    {[1, 2, 3].map(i => (
                        <div key={i} className={[styles.skeletonCard, styles.spacing2].join(' ')}></div>
                    ))}
                </div>
            ) : error ? (
                <InlineError message={error} onRetry={() => { setLoading(true); setError(null); setReloadKey(key => key + 1); }} />
            ) : filteredLocations.length === 0 ? (
                <div className={styles.tone2}>
                    Chưa có cơ sở nào trong khu vực đã chọn.
                </div>
            ) : (
                <div className={styles.grid3}>
                    {filteredLocations.map(loc => (
                        <div key={loc.id} className={styles.card}>
                            <div className={styles.flexLayout13}>
                                <div>
                                    <span className={styles.text9}>
                                        {loc.city}
                                    </span>
                                    <h2 className={[styles.cardTitle, styles.heading4].join(' ')}>
                                        {loc.name}
                                    </h2>
                                </div>
                            </div>

                            <div className={styles.stackLayout1}>
                                <div className={styles.flexLayout10}>{loc.address}</div>
                                {loc.openingHours && <div className={styles.flexLayout14}>Giờ làm việc: {loc.openingHours}</div>}
                                {loc.phone && <div className={styles.flexLayout14}>Điện thoại: <a href={`tel:${loc.phone.replace(/\s+/g, '')}`}>{loc.phone}</a></div>}

                                {loc.features && loc.features.length > 0 && (
                                    <div className={styles.flexLayout15}>
                                        {loc.features.map((srv, idx) => (
                                            <span
                                                key={idx}
                                                className={styles.text10}
                                            >
                                                {srv}
                                            </span>
                                        ))}
                                    </div>
                                )}
                            </div>

                            <div className={styles.cardFooter}>
                                <a 
                                    href={`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(loc.name + ' ' + loc.address)}`}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className={styles.cardActionLink}
                                >
                                    Chỉ đường
                                </a>
                                <LinkButton type="primary" to="/patient/book">Đặt lịch khám</LinkButton>
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
};
