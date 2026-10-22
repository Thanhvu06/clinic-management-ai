import { useContext, useEffect, useRef } from 'react';
import { UNSAFE_LocationContext } from 'react-router-dom';

// Trang còn được render ngoài Router (test cũ), nên đọc LocationContext thay vì useSearchParams vốn ném lỗi khi thiếu Router.
export function useOpenPrescriptionFromQuery(listLoaded: boolean, openPrescription: (id: number) => void) {
    const search = useContext(UNSAFE_LocationContext)?.location.search ?? '';
    const requestedId = new URLSearchParams(search).get('id');
    const handledRef = useRef<string | null>(null);

    useEffect(() => {
        if (!listLoaded || !requestedId || handledRef.current === requestedId) return;
        handledRef.current = requestedId;
        const id = Number(requestedId);
        if (Number.isInteger(id) && id > 0) openPrescription(id);
    }, [listLoaded, requestedId, openPrescription]);
}
