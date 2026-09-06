import React from 'react';

interface Column<T> {
    header: string;
    accessor?: keyof T | ((item: T) => React.ReactNode);
    className?: string;
    style?: React.CSSProperties;
}

interface DataTableProps<T> {
    columns: Column<T>[];
    data: T[];
    keyExtractor: (item: T) => string | number;
    emptyText?: string;
    onRowClick?: (item: T) => void;
}

export function DataTable<T>({
    columns,
    data,
    keyExtractor,
    emptyText = 'Không có dữ liệu hiển thị.',
    onRowClick
}: DataTableProps<T>) {
    if (!data || data.length === 0) {
        return (
            <div className="table-responsive" style={{ padding: '32px', textAlign: 'center', color: 'var(--c-text-light)' }}>
                {emptyText}
            </div>
        );
    }

    return (
        <div className="table-responsive">
            <table className="table">
                <thead>
                    <tr>
                        {columns.map((col, idx) => (
                            <th key={idx} className={col.className} style={col.style}>
                                {col.header}
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {data.map((item) => (
                        <tr 
                            key={keyExtractor(item)} 
                            onClick={onRowClick ? () => onRowClick(item) : undefined}
                            style={{ cursor: onRowClick ? 'pointer' : 'default' }}
                        >
                            {columns.map((col, cIdx) => (
                                <td key={cIdx} className={col.className} style={col.style}>
                                    {typeof col.accessor === 'function'
                                        ? col.accessor(item)
                                        : col.accessor
                                        ? (item[col.accessor] as unknown as React.ReactNode)
                                        : null}
                                </td>
                            ))}
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}
