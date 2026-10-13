// Standard Code 39 narrow/wide patterns, including the start/stop asterisk.
// Pattern reference: https://github.com/zxing/zxing/blob/master/core/src/main/java/com/google/zxing/oned/Code39Reader.java
const alphabet = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%*';
const patterns = [
    52, 289, 97, 352, 49, 304, 112, 37, 292, 100,
    265, 73, 328, 25, 280, 88, 13, 268, 76, 28,
    259, 67, 322, 19, 274, 82, 7, 262, 70, 22,
    385, 193, 448, 145, 400, 208, 133, 388, 196, 168, 162, 138, 42, 148,
];
export interface Code39Bar { x: number; width: number; }
export function encodeCode39(value: string): { bars: Code39Bar[]; width: number } {
    if (!value || [...value].some(character => character === '*' || !alphabet.includes(character))) {
        throw new Error('Mã vạch Code 39 chỉ hỗ trợ chữ hoa, số và ký tự tiêu chuẩn.');
    }
    const bars: Code39Bar[] = [];
    const framed = `*${value}*`;
    let x = 10; // Quiet zones are ten narrow modules on both sides.
    [...framed].forEach((character, index) => {
        const pattern = patterns[alphabet.indexOf(character)];
        for (let element = 0; element < 9; element++) {
            const width = pattern & (1 << (8 - element)) ? 3 : 1;
            if (element % 2 === 0) bars.push({ x, width });
            x += width;
        }
        if (index < framed.length - 1) x++; // One narrow inter-character gap.
    });
    return { bars, width: x + 10 };
}
