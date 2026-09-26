import { describe, expect, it } from "vitest";
import { buildRevisitBookingUrl } from "../utils/revisitBookingHelper";

describe("buildRevisitBookingUrl", () => {
    it("builds a booking URL bound to the revisit request", () => {
        expect(buildRevisitBookingUrl({
            id: 12,
            specialtyId: 3,
            doctorId: 7,
            suggestedDate: "2026-09-12"
        })).toBe(
            "/patient/book?revisitRequestId=12&specialtyId=3&doctorId=7&date=2026-09-12"
        );
    });

    it("normalizes an ISO date-time to a date query parameter", () => {
        expect(buildRevisitBookingUrl({
            id: 2,
            specialtyId: 5,
            doctorId: 9,
            suggestedDate: "2026-10-01T00:00:00"
        })).toContain("date=2026-10-01");
    });

    it("rejects incomplete or untrusted identifiers", () => {
        expect(buildRevisitBookingUrl({
            id: 0,
            specialtyId: 3,
            doctorId: 7,
            suggestedDate: "2026-09-12"
        })).toBeNull();

        expect(buildRevisitBookingUrl({
            id: 12,
            specialtyId: 0,
            doctorId: 7,
            suggestedDate: "not-a-date"
        })).toBeNull();
    });
});
