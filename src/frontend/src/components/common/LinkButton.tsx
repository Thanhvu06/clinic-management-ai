import React from 'react';
import { Button, type ButtonProps } from 'antd';
import { useHref, useLinkClickHandler } from 'react-router-dom';

type LinkButtonProps = Omit<ButtonProps, 'href' | 'htmlType'> & { to: string };

// Nút antd hiển thị như thẻ <a>: giữ vai trò link cho trình đọc màn hình và điều hướng SPA không tải lại trang.
export const LinkButton: React.FC<LinkButtonProps> = ({ to, onClick, ...props }) => {
    const href = useHref(to);
    const navigate = useLinkClickHandler<HTMLAnchorElement>(to);
    return (
        <Button
            {...props}
            href={href}
            onClick={event => {
                onClick?.(event);
                if (!event.defaultPrevented) navigate(event as React.MouseEvent<HTMLAnchorElement>);
            }}
        />
    );
};
