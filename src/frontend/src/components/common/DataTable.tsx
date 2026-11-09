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
function DataTableComponent<T>({
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
    const tableColumns = useMemo<TableColumnsType<T>>(() => columns.map((col, idx) => ({
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
    })), [columns]);
    const dataSource = useMemo(() => data ?? [], [data]);
    const locale = useMemo(() => ({ emptyText }), [emptyText]);
    const components = useMemo(() => accessibleTable ? { table: accessibleTable } : undefined, [accessibleTable]);
    const onRow = useMemo(() => onRowClick
        ? (item: T) => ({ onClick: () => onRowClick(item) })
        : undefined, [onRowClick]);

    return (
        <Table<T>
            className={styles.dataTable}
            components={components}
            columns={tableColumns}
            dataSource={dataSource}
            rowKey={keyExtractor}
            bordered
            pagination={false}
            tableLayout="auto"
            locale={locale}
            rowClassName={onRowClick ? styles.dataTableClickableRow : undefined}
            onRow={onRow}
        />
    );
}

export const DataTable = React.memo(DataTableComponent) as typeof DataTableComponent;
