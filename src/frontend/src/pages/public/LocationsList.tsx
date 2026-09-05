import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { MapPin, Phone, Clock, Calendar, Navigation, Building } from 'lucide-react';
import { CLINIC_LOCATIONS, type ClinicLocation } from '../../data/locationsData';
import styles from './PublicPages.module.css';

export const LocationsList: React.FC = () => {
    const [selectedCity, setSelectedCity] = useState<string>('all');

    const cities = [
        { id: 'all', name: 'Tất cả cơ sở' },
        { id: 'hcm', name: 'TP. Hồ Chí Minh' },
        { id: 'hn', name: 'Hà Nội' },
        { id: 'dn', name: 'Đà Nẵng' }
    ];

    const filteredLocations = CLINIC_LOCATIONS.filter(loc => {
        if (selectedCity === 'all') return true;
        if (selectedCity === 'hcm') return loc.address.includes('Hồ Chí Minh');
        if (selectedCity === 'hn') return loc.address.includes('Hà Nội');
        if (selectedCity === 'dn') return loc.address.includes('Đà Nẵng');
        return true;
    });

    return (
        <div className={styles.pageContainer}>
            <div className={styles.pageHeader}>
                <nav className={styles.breadcrumb} aria-label="Breadcrumb">
                    <Link to="/">Trang chủ</Link>
                    <span>/</span>
                    <span aria-current="page">Hệ thống cơ sở phòng khám</span>
                </nav>
                <h1 className={styles.pageTitle}>Hệ thống Cơ sở & Điểm khám ClinicCare</h1>
                <p className={styles.pageSubtitle}>
                    Mạng lưới phòng khám đa khoa hiện đại, tọa lạc tại các vị trí trung tâm, giao thông thuận tiện và trang bị cơ sở vật chất đồng bộ đạt chuẩn quốc tế.
                </p>
            </div>

            <div className={styles.toolbar}>
                <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                    {cities.map(c => (
                        <button
                            key={c.id}
                            type="button"
                            onClick={() => setSelectedCity(c.id)}
                            style={{
                                padding: '8px 18px',
                                borderRadius: '20px',
                                border: '1px solid',
                                borderColor: selectedCity === c.id ? 'var(--c-primary)' : 'var(--c-border)',
                                background: selectedCity === c.id ? 'var(--c-primary)' : '#ffffff',
                                color: selectedCity === c.id ? '#ffffff' : 'var(--c-text)',
                                fontWeight: 600,
                                fontSize: '0.9rem',
                                cursor: 'pointer',
                                transition: 'all 0.2s'
                            }}
                        >
                            {c.name}
                        </button>
                    ))}
                </div>
                <div className={styles.resultCount}>
                    Hiển thị {filteredLocations.length} cơ sở
                </div>
            </div>

            <div className={styles.grid3}>
                {filteredLocations.map((loc: ClinicLocation) => (
                    <div key={loc.id} className={styles.card}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '14px' }}>
                            <div className={styles.cardIcon} style={{ margin: 0, background: 'rgba(14, 116, 144, 0.08)' }}>
                                <Building size={24} />
                            </div>
                            <div>
                                <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--c-primary)', textTransform: 'uppercase' }}>
                                    {loc.city}
                                </span>
                                <h2 className={styles.cardTitle} style={{ fontSize: '1.15rem', margin: 0 }}>
                                    {loc.name}
                                </h2>
                            </div>
                        </div>

                        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', margin: '12px 0 20px 0', flexGrow: 1 }}>
                            <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', fontSize: '0.9rem', color: 'var(--c-text)' }}>
                                <MapPin size={16} color="var(--c-primary)" style={{ flexShrink: 0, marginTop: '2px' }} />
                                <span>{loc.address}</span>
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem', color: 'var(--c-text-muted)' }}>
                                <Clock size={16} color="var(--c-text-muted)" style={{ flexShrink: 0 }} />
                                <span>{loc.hours}</span>
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem', color: 'var(--c-text-muted)' }}>
                                <Phone size={16} color="var(--c-text-muted)" style={{ flexShrink: 0 }} />
                                <span>{loc.phone}</span>
                            </div>
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
                                className="btn btn-primary btn-sm"
                                style={{ textDecoration: 'none' }}
                            >
                                <Calendar size={14} /> Đặt khám tại đây
                            </Link>
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
};
