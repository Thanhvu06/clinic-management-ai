import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { MapPin, Phone, Clock, Calendar, Navigation, Building } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import type { ClinicLocationDto } from '../../types';
import styles from './PublicPages.module.css';

export const LocationsList: React.FC = () => {
    const [locations, setLocations] = useState<ClinicLocationDto[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [selectedCity, setSelectedCity] = useState<string>('all');

    useEffect(() => {
        const fetchLocations = async () => {
            try {
                setLoading(true);
                const res = await axiosClient.get<any, any>('/locations');
                if (res.success && res.data) {
                    setLocations(res.data);
                } else {
                    setError('Không thể tải danh sách cơ sở phòng khám.');
                }
            } catch (err) {
                console.error('Failed to fetch clinic locations:', err);
                setError('Đã xảy ra lỗi khi kết nối máy chủ.');
            } finally {
                setLoading(false);
            }
        };

        fetchLocations();
    }, []);

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
                    <span aria-current="page">Hệ thống cơ sở phòng khám</span>
                </nav>
                <h1 className={styles.pageTitle}>Hệ thống Cơ sở & Điểm khám ClinicCare</h1>
                <p className={styles.pageSubtitle}>
                    Mạng lưới phòng khám đa khoa hiện đại, tọa lạc tại các vị trí trung tâm, giao thông thuận tiện và trang bị cơ sở vật chất đồng bộ phục vụ người bệnh.
                </p>
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
                <div className={styles.tone1}>
                    <p>{error}</p>
                    <button
                        type="button"
                        className={["btn btn-primary btn-sm", styles.button3].join(' ')}
                        onClick={() => window.location.reload()}
                    >
                        Thử lại
                    </button>
                </div>
            ) : filteredLocations.length === 0 ? (
                <div className={styles.tone2}>
                    Chưa có cơ sở nào trong khu vực đã chọn.
                </div>
            ) : (
                <div className={styles.grid3}>
                    {filteredLocations.map(loc => (
                        <div key={loc.id} className={styles.card}>
                            <div className={styles.flexLayout13}>
                                <div className={[styles.cardIcon, styles.surface4].join(' ')}>
                                    <Building size={24} />
                                </div>
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
                                <div className={styles.flexLayout10}>
                                    <MapPin size={16} color="var(--cc-color-primary)" className={styles.icon4} />
                                    <span>{loc.address}</span>
                                </div>
                                <div className={styles.flexLayout14}>
                                    <Clock size={16} color="var(--cc-color-text-muted)" className={styles.icon7} />
                                    <span>{loc.openingHours}</span>
                                </div>
                                <div className={styles.flexLayout14}>
                                    <Phone size={16} color="var(--cc-color-text-muted)" className={styles.icon7} />
                                    <span>{loc.phone}</span>
                                </div>

                                {loc.services && loc.services.length > 0 && (
                                    <div className={styles.flexLayout15}>
                                        {loc.services.map((srv, idx) => (
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
                                    <Navigation size={14} /> Chỉ đường
                                </a>
                                <Link 
                                    to="/patient/book"
                                    className={["btn btn-primary btn-sm", styles.icon8].join(' ')}
                                >
                                    <Calendar size={14} /> Đặt khám
                                </Link>
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
};
