import React from 'react';
import { Link } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';

interface BreadcrumbItem {
    label: string;
    path?: string;
}

interface BreadcrumbProps {
    items: BreadcrumbItem[];
}

export const Breadcrumb: React.FC<BreadcrumbProps> = ({ items }) => {
    return (
        <nav style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '24px', fontSize: '0.9rem', color: 'var(--c-text-light)' }}>
            {items.map((item, index) => {
                const isLast = index === items.length - 1;
                return (
                    <React.Fragment key={index}>
                        {item.path && !isLast ? (
                            <Link to={item.path} style={{ color: 'var(--c-text-light)', textDecoration: 'none', transition: 'color 0.2s' }} onMouseOver={(e) => e.currentTarget.style.color = 'var(--c-primary)'} onMouseOut={(e) => e.currentTarget.style.color = 'var(--c-text-light)'}>
                                {item.label}
                            </Link>
                        ) : (
                            <span style={{ color: isLast ? 'var(--c-navy)' : 'inherit', fontWeight: isLast ? 600 : 400 }}>
                                {item.label}
                            </span>
                        )}
                        {!isLast && <ChevronRight size={16} />}
                    </React.Fragment>
                );
            })}
        </nav>
    );
};
