import type { ThemeConfig } from 'antd';

/**
 * Nguồn duy nhất cho design tokens của ClinicCare AI.
 * - antd đọc qua `antdTheme` (ConfigProvider trong main.tsx).
 * - CSS cũ đọc qua biến `--cc-*` do `applyTokenCssVariables` ghi lên :root;
 *   các biến `--c-*`, `--space-*`, `--radius-*` trong index.css chỉ là alias trỏ về đây.
 * Muốn đổi màu/khoảng cách/kích thước: sửa ở file này, không sửa giá trị trong CSS.
 */

export const colors = {
    primary: '#0F4C81',
    primaryHover: '#0A355C',
    primaryLight: '#EBF3FA',
    secondary: '#0D9488',
    secondaryHover: '#0F766E',
    teal: '#0891B2',
    tealLight: '#E0F2FE',
    tealDark: '#0E7490',
    navy: '#0F4C81',
    navyDark: '#0C2340',

    text: '#172033',
    textDark: '#0F172A',
    textSecondary: '#667085',
    textMuted: '#94A3B8',
    textOnDark: '#F8FAFC',
    textOnDarkMuted: '#94A3B8',

    border: '#E2E8F0',
    borderLight: '#F1F5F9',

    bgLayout: '#F4F7FA',
    bgContainer: '#FFFFFF',
    bgSubtle: '#F8FAFC',

    success: '#059669',
    successBg: '#ECFDF5',
    warning: '#D97706',
    warningBg: '#FFFBEB',
    danger: '#DC2626',
    dangerBg: '#FEF2F2',
    info: '#0284C7',
    infoBg: '#F0F9FF',
} as const;

export const spacing = {
    xxs: 4,
    xs: 8,
    sm: 12,
    md: 16,
    lg: 24,
    xl: 32,
} as const;

export const radius = {
    sm: 6,
    md: 8,
    lg: 12,
    xl: 14,
    full: 9999,
} as const;

export const layout = {
    controlHeight: 40,
    tableRowHeight: 44,
    sidebarWidth: 240,
    headerHeight: 56,
    contentPadding: 24,
    gap: 8,
} as const;

export const typography = {
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif",
    fontSize: 14,
    lineHeight: 22,
} as const;

export const shadows = {
    sm: '0 1px 2px 0 rgba(16, 24, 40, 0.05)',
    md: '0 4px 8px -2px rgba(16, 24, 40, 0.06), 0 2px 4px -2px rgba(16, 24, 40, 0.04)',
    lg: '0 12px 16px -4px rgba(16, 24, 40, 0.08), 0 4px 6px -2px rgba(16, 24, 40, 0.03)',
} as const;

export const tokens = { colors, spacing, radius, layout, typography, shadows } as const;

const px = (value: number) => `${value}px`;

/** Biến CSS `--cc-*` sinh từ tokens; index.css alias các biến cũ về đây. */
export const tokenCssVariables: Record<string, string> = {
    '--cc-color-primary': colors.primary,
    '--cc-color-primary-hover': colors.primaryHover,
    '--cc-color-primary-light': colors.primaryLight,
    '--cc-color-secondary': colors.secondary,
    '--cc-color-secondary-hover': colors.secondaryHover,
    '--cc-color-teal': colors.teal,
    '--cc-color-teal-light': colors.tealLight,
    '--cc-color-teal-dark': colors.tealDark,
    '--cc-color-navy': colors.navy,
    '--cc-color-navy-dark': colors.navyDark,
    '--cc-color-text': colors.text,
    '--cc-color-text-dark': colors.textDark,
    '--cc-color-text-secondary': colors.textSecondary,
    '--cc-color-text-muted': colors.textMuted,
    '--cc-color-text-on-dark': colors.textOnDark,
    '--cc-color-text-on-dark-muted': colors.textOnDarkMuted,
    '--cc-color-border': colors.border,
    '--cc-color-border-light': colors.borderLight,
    '--cc-color-bg-layout': colors.bgLayout,
    '--cc-color-bg-container': colors.bgContainer,
    '--cc-color-bg-subtle': colors.bgSubtle,
    '--cc-color-success': colors.success,
    '--cc-color-success-bg': colors.successBg,
    '--cc-color-warning': colors.warning,
    '--cc-color-warning-bg': colors.warningBg,
    '--cc-color-danger': colors.danger,
    '--cc-color-danger-bg': colors.dangerBg,
    '--cc-color-info': colors.info,
    '--cc-color-info-bg': colors.infoBg,
    '--cc-space-xxs': px(spacing.xxs),
    '--cc-space-xs': px(spacing.xs),
    '--cc-space-sm': px(spacing.sm),
    '--cc-space-md': px(spacing.md),
    '--cc-space-lg': px(spacing.lg),
    '--cc-space-xl': px(spacing.xl),
    '--cc-radius-sm': px(radius.sm),
    '--cc-radius-md': px(radius.md),
    '--cc-radius-lg': px(radius.lg),
    '--cc-radius-xl': px(radius.xl),
    '--cc-radius-full': px(radius.full),
    '--cc-control-height': px(layout.controlHeight),
    '--cc-table-row-height': px(layout.tableRowHeight),
    '--cc-sidebar-width': px(layout.sidebarWidth),
    '--cc-header-height': px(layout.headerHeight),
    '--cc-content-padding': px(layout.contentPadding),
    '--cc-gap': px(layout.gap),
    '--cc-font-family': typography.fontFamily,
    '--cc-shadow-sm': shadows.sm,
    '--cc-shadow-md': shadows.md,
    '--cc-shadow-lg': shadows.lg,
};

export function applyTokenCssVariables(target: HTMLElement = document.documentElement) {
    for (const [name, value] of Object.entries(tokenCssVariables)) {
        target.style.setProperty(name, value);
    }
}

/** Padding dọc của ô bảng để mỗi dòng cao đúng `tableRowHeight` (cộng 1px viền dưới). */
const tableCellPaddingBlock = (layout.tableRowHeight - typography.lineHeight - 1) / 2;

export const antdTheme: ThemeConfig = {
    token: {
        colorPrimary: colors.primary,
        colorInfo: colors.info,
        colorSuccess: colors.success,
        colorWarning: colors.warning,
        colorError: colors.danger,
        colorText: colors.text,
        colorTextHeading: colors.textDark,
        colorTextSecondary: colors.textSecondary,
        colorTextTertiary: colors.textMuted,
        colorBorder: colors.border,
        colorBorderSecondary: colors.border,
        colorBgLayout: colors.bgLayout,
        colorBgContainer: colors.bgContainer,
        colorLink: colors.primary,
        colorLinkHover: colors.primaryHover,
        fontFamily: typography.fontFamily,
        fontSize: typography.fontSize,
        borderRadius: radius.md,
        borderRadiusSM: radius.sm,
        borderRadiusLG: radius.lg,
        controlHeight: layout.controlHeight,
        padding: spacing.md,
        paddingXS: spacing.xs,
        paddingSM: spacing.sm,
        paddingLG: spacing.lg,
        paddingXL: spacing.xl,
        margin: spacing.md,
        marginXS: spacing.xs,
        marginSM: spacing.sm,
        marginLG: spacing.lg,
        marginXL: spacing.xl,
        boxShadowTertiary: shadows.sm,
    },
    components: {
        Layout: {
            siderBg: colors.navyDark,
            headerBg: colors.bgContainer,
            headerHeight: layout.headerHeight,
            headerPadding: `0 ${px(layout.contentPadding)}`,
            bodyBg: colors.bgLayout,
        },
        Menu: {
            darkItemBg: colors.navyDark,
            darkSubMenuItemBg: colors.navyDark,
            darkItemColor: colors.textOnDarkMuted,
            darkItemHoverColor: colors.textOnDark,
            // secondaryHover thay vì secondary để chữ trắng đạt tương phản ≥ 4.5:1.
            darkItemSelectedBg: colors.secondaryHover,
            darkItemSelectedColor: '#FFFFFF',
            itemHeight: layout.controlHeight,
            itemMarginInline: spacing.xs,
            itemBorderRadius: radius.md,
        },
        Table: {
            headerBg: colors.bgSubtle,
            headerColor: colors.textSecondary,
            headerSplitColor: colors.border,
            borderColor: colors.border,
            rowHoverBg: colors.bgSubtle,
            cellPaddingBlock: tableCellPaddingBlock,
            cellPaddingInline: spacing.md,
        },
        Card: {
            paddingLG: spacing.lg,
        },
    },
};
