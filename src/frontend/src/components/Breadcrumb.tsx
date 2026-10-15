import React from 'react';
import { Link } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';
import styles from './Breadcrumb.module.css';

interface BreadcrumbItem {
    label: string;
    path?: string;
}

interface BreadcrumbProps {
    items: BreadcrumbItem[];
}

export const Breadcrumb: React.FC<BreadcrumbProps> = ({ items }) => {
    return (
        <nav className={styles.flexLayout1}>
            {items.map((item, index) => {
                const isLast = index === items.length - 1;
                return (
                    <React.Fragment key={index}>
                        {item.path && !isLast ? (
                            <Link to={item.path} className={styles.icon1} onMouseOver={(e) => e.currentTarget.style.color = 'var(--cc-color-primary)'} onMouseOut={(e) => e.currentTarget.style.color = 'var(--cc-color-text-secondary)'}>
                                {item.label}
                            </Link>
                        ) : (
                            <span className={[(isLast ? styles.state1 : styles.state2), (isLast ? styles.state3 : styles.state4)].join(' ')}>
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
