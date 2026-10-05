import React from 'react';
import { ConfigProvider } from 'antd';
import viVN from 'antd/locale/vi_VN';
import { antdTheme } from './tokens';

export const AppThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <ConfigProvider locale={viVN} theme={antdTheme}>
        {children}
    </ConfigProvider>
);
