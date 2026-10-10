import React, { useMemo } from 'react';
import { Table } from 'antd';
import type { TableColumnsType } from 'antd';
import '../../theme/browserCompat';
import styles from './common.module.css';

export interface DataTableColumn<T> {
    header: string;
    accessor?: keyof T | ((item: T) => React.ReactNode);
    className?: string;
    style?: React.CSSProperties;
    /** Căn lề cột (tiêu đề và ô cùng căn). Mặc định: trái. */
    align?: 'left' | 'center' | 'right';
    width?: number | string;
}

interface DataTableProps<T> {
    columns: DataTableColumn<T>[];
    data: T[];
    keyExtractor: (item: T) => string | number;
    emptyText?: string;
    onRowClick?: (item: T) => void;
    ariaLabel?: string;
}

/**
 * Bảng chuẩn của hệ thống: antd Table có viền, tiêu đề cột tách rõ, chiều cao dòng theo token (44px).
 */
export function DataTable<T>({
    columns,
    data,
    keyExtractor,
    emptyText = 'Không có dữ liệu hiển thị.',
    onRowClick,
    ariaLabel
}: DataTableProps<T>) {
    const accessibleTable = useMemo(() => ariaLabel
        ? (props: React.HTMLAttributes<HTMLTableElement>) => <table {...props} aria-label={ariaLabel} />
        : undefined, [ariaLabel]);
    const tableColumns: TableColumnsType<T> = columns.map((col, idx) => ({
        key: idx,
        title: col.header,
        className: col.className,
        align: col.align,
        width: col.width,
        onHeaderCell: col.style ? () => ({ style: col.style }) : undefined,
        onCell: col.style ? () => ({ style: col.style }) : undefined,
        render: (_: unknown, item: T) =>
            typeof col.accessor === 'function'
                ? col.accessor(item)
                : col.accessor
                ? (item[col.accessor] as unknown as React.ReactNode)
                : null,
    }));

    return (
        <Table<T>
            className={styles.dataTable}
            components={accessibleTable ? { table: accessibleTable } : undefined}
            columns={tableColumns}
            dataSource={data ?? []}
            rowKey={keyExtractor}
            bordered
            pagination={false}
            tableLayout="auto"
            locale={{ emptyText }}
            rowClassName={onRowClick ? styles.dataTableClickableRow : undefined}
            onRow={onRowClick ? (item) => ({ onClick: () => onRowClick(item) }) : undefined}
        />
    );
}
