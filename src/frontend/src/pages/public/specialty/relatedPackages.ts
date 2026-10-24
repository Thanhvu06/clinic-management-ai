import type { LandingPackage } from '../landing/landingTypes';

export function relatedPackages(packages: LandingPackage[], name: string) {
    const keywords = name.toLocaleLowerCase('vi-VN').replace(/^(khoa|nội)\s+/, '');
    return packages.filter(pkg => pkg.isActive && `${pkg.name} ${pkg.targetAudience}`.toLocaleLowerCase('vi-VN').includes(keywords)).slice(0, 3);
}
