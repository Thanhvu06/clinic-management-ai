import React, { useState, useEffect } from 'react';
import { Plus, CheckCircle, XCircle, Layers, DoorOpen, BedDouble } from 'lucide-react';
import { organizationApi, type FacilityDto, type DepartmentDto, type RoomDto, type BedDto } from '../../api/organizationApi';
import { useDialog } from '../../contexts/DialogContext';

export const AdminFacilities: React.FC = () => {
    const { showAlert } = useDialog();
    const [facilities, setFacilities] = useState<FacilityDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [selectedFacility, setSelectedFacility] = useState<FacilityDto | null>(null);
    const [departments, setDepartments] = useState<DepartmentDto[]>([]);
    const [rooms, setRooms] = useState<RoomDto[]>([]);
    const [beds, setBeds] = useState<BedDto[]>([]);
    const [selectedRoom, setSelectedRoom] = useState<RoomDto | null>(null);

    // Modals
    const [facilityModalOpen, setFacilityModalOpen] = useState(false);
    const [facilityForm, setFacilityForm] = useState({
        code: '',
        name: '',
        address: '',
        city: '',
        phone: '',
        hospitalLevel: 'Hạng 1',
        description: ''
    });

    const [deptModalOpen, setDeptModalOpen] = useState(false);
    const [deptForm, setDeptForm] = useState({
        code: '',
        name: '',
        departmentType: 1,
        description: ''
    });

    const [roomModalOpen, setRoomModalOpen] = useState(false);
    const [roomForm, setRoomForm] = useState({
        departmentId: 0,
        roomNumber: '',
        name: '',
        roomType: 2,
        floorNumber: 1,
        maxCapacity: 4
    });

    const [bedModalOpen, setBedModalOpen] = useState(false);
    const [bedForm, setBedForm] = useState({
        roomId: 0,
        bedNumber: '',
        bedType: 1,
        dailyRate: 200000,
        notes: ''
    });

    const fetchFacilities = async () => {
        setLoading(true);
        try {
            const res = await organizationApi.getFacilities(true);
            if (res.success && res.data) {
                setFacilities(res.data);
                if (res.data.length > 0 && !selectedFacility) {
                    selectFacility(res.data[0]);
                }
            }
        } catch (error: any) {
            showAlert('Lỗi', 'Không thể tải danh sách cơ sở bệnh viện.', 'error');
        } finally {
            setLoading(false);
        }
    };

    const selectFacility = async (fac: FacilityDto) => {
        setSelectedFacility(fac);
        setSelectedRoom(null);
        setBeds([]);
        try {
            const deptRes = await organizationApi.getDepartments(fac.id);
            if (deptRes.success && deptRes.data) {
                setDepartments(deptRes.data);
            }
            const roomRes = await organizationApi.getRooms({ facilityId: fac.id });
            if (roomRes.success && roomRes.data) {
                setRooms(roomRes.data);
            }
        } catch (err) {
            console.error(err);
        }
    };

    const selectRoom = async (room: RoomDto) => {
        setSelectedRoom(room);
        try {
            const res = await organizationApi.getBedsByRoom(room.id);
            if (res.success && res.data) {
                setBeds(res.data);
            }
        } catch (err) {
            console.error(err);
        }
    };

    useEffect(() => {
        fetchFacilities();
    }, []);

    const handleCreateFacility = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const res = await organizationApi.createFacility(facilityForm);
            if (res.success) {
                showAlert('Thành công', 'Thêm cơ sở y tế thành công!', 'success');
                setFacilityModalOpen(false);
                setFacilityForm({ code: '', name: '', address: '', city: '', phone: '', hospitalLevel: 'Hạng 1', description: '' });
                fetchFacilities();
            }
        } catch (err: any) {
            showAlert('Lỗi', err.response?.data?.message || 'Không thể tạo cơ sở.', 'error');
        }
    };

    const handleCreateDepartment = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!selectedFacility) return;
        try {
            const res = await organizationApi.createDepartment({
                facilityId: selectedFacility.id,
                ...deptForm
            });
            if (res.success) {
                showAlert('Thành công', 'Thêm khoa phòng thành công!', 'success');
                setDeptModalOpen(false);
                setDeptForm({ code: '', name: '', departmentType: 1, description: '' });
                selectFacility(selectedFacility);
            }
        } catch (err: any) {
            showAlert('Lỗi', err.response?.data?.message || 'Không thể tạo khoa phòng.', 'error');
        }
    };

    const handleCreateRoom = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const res = await organizationApi.createRoom(roomForm);
            if (res.success) {
                showAlert('Thành công', 'Thêm phòng thành công!', 'success');
                setRoomModalOpen(false);
                if (selectedFacility) selectFacility(selectedFacility);
            }
        } catch (err: any) {
            showAlert('Lỗi', err.response?.data?.message || 'Không thể tạo phòng.', 'error');
        }
    };

    const handleCreateBed = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!selectedRoom) return;
        try {
            const res = await organizationApi.createBed({
                ...bedForm,
                roomId: selectedRoom.id
            });
            if (res.success) {
                showAlert('Thành công', 'Thêm giường bệnh thành công!', 'success');
                setBedModalOpen(false);
                selectRoom(selectedRoom);
            }
        } catch (err: any) {
            showAlert('Lỗi', err.response?.data?.message || 'Không thể tạo giường bệnh.', 'error');
        }
    };

    return (
        <div style={{ padding: '24px', maxWidth: '1400px', margin: '0 auto' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <div>
                    <h1 style={{ fontSize: '24px', fontWeight: 'bold', color: '#1e293b' }}>Quản trị Mạng lưới Bệnh viện & Cơ sở</h1>
                    <p style={{ color: '#64748b', marginTop: '4px' }}>Quản lý cây tổ chức: Cơ sở ➔ Tòa nhà ➔ Khoa phòng ➔ Phòng bệnh ➔ Giường bệnh</p>
                </div>
                <button
                    onClick={() => setFacilityModalOpen(true)}
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        background: '#0284c7',
                        color: '#fff',
                        padding: '10px 18px',
                        borderRadius: '8px',
                        border: 'none',
                        cursor: 'pointer',
                        fontWeight: 600
                    }}
                >
                    <Plus size={18} /> Thêm Cơ sở Mới
                </button>
            </div>

            {loading && facilities.length === 0 && (
                <div style={{ padding: '40px', textAlign: 'center', color: '#64748b' }}>Đang tải dữ liệu mạng lưới cơ sở...</div>
            )}

            {/* Facilities Cards */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: '16px', marginBottom: '28px' }}>
                {facilities.map(fac => {
                    const isSelected = selectedFacility?.id === fac.id;
                    return (
                        <div
                            key={fac.id}
                            onClick={() => selectFacility(fac)}
                            style={{
                                border: isSelected ? '2px solid #0284c7' : '1px solid #e2e8f0',
                                borderRadius: '12px',
                                padding: '20px',
                                background: isSelected ? '#f0f9ff' : '#fff',
                                cursor: 'pointer',
                                transition: 'all 0.2s',
                                boxShadow: '0 1px 3px rgba(0,0,0,0.05)'
                            }}
                        >
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                <div>
                                    <span style={{ fontSize: '12px', fontWeight: 'bold', color: '#0284c7', background: '#e0f2fe', padding: '2px 8px', borderRadius: '4px' }}>{fac.code}</span>
                                    <h3 style={{ fontSize: '18px', fontWeight: 600, color: '#1e293b', marginTop: '6px' }}>{fac.name}</h3>
                                </div>
                                {fac.isActive ? (
                                    <span style={{ color: '#16a34a', fontSize: '13px', display: 'flex', alignItems: 'center', gap: '4px' }}><CheckCircle size={15} /> Hoạt động</span>
                                ) : (
                                    <span style={{ color: '#dc2626', fontSize: '13px', display: 'flex', alignItems: 'center', gap: '4px' }}><XCircle size={15} /> Tạm dừng</span>
                                )}
                            </div>
                            <p style={{ fontSize: '14px', color: '#64748b', marginTop: '8px' }}>{fac.address}, {fac.city}</p>
                            <div style={{ display: 'flex', gap: '16px', marginTop: '14px', borderTop: '1px solid #e2e8f0', paddingTop: '12px', fontSize: '13px', color: '#475569' }}>
                                <span>🏢 {fac.buildingCount} Tòa nhà</span>
                                <span>🩺 {fac.departmentCount} Khoa/Phòng</span>
                                <span>📞 {fac.phone}</span>
                            </div>
                        </div>
                    );
                })}
            </div>

            {/* Facility Details Breakdown */}
            {selectedFacility && (
                <div style={{ background: '#fff', borderRadius: '12px', border: '1px solid #e2e8f0', padding: '24px', boxShadow: '0 1px 3px rgba(0,0,0,0.05)' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid #e2e8f0', paddingBottom: '16px', marginBottom: '20px' }}>
                        <div>
                            <h2 style={{ fontSize: '20px', fontWeight: 'bold', color: '#0f172a' }}>Cấu trúc Khoa phòng & Buồng Giường: {selectedFacility.name}</h2>
                            <p style={{ fontSize: '14px', color: '#64748b' }}>Phân cấp chi tiết các đơn vị điều trị trực thuộc</p>
                        </div>
                        <div style={{ display: 'flex', gap: '12px' }}>
                            <button
                                onClick={() => {
                                    setDeptForm({ code: '', name: '', departmentType: 1, description: '' });
                                    setDeptModalOpen(true);
                                }}
                                style={{ display: 'flex', alignItems: 'center', gap: '6px', background: '#0284c7', color: '#fff', padding: '8px 14px', borderRadius: '6px', border: 'none', cursor: 'pointer', fontSize: '14px', fontWeight: 500 }}
                            >
                                <Plus size={16} /> Thêm Khoa Phòng
                            </button>
                            <button
                                onClick={() => {
                                    if (departments.length > 0) {
                                        setRoomForm({ departmentId: departments[0].id, roomNumber: '', name: '', roomType: 2, floorNumber: 1, maxCapacity: 4 });
                                        setRoomModalOpen(true);
                                    } else {
                                        showAlert('Thông báo', 'Cần tạo ít nhất 1 khoa phòng trước khi tạo phòng.', 'warning');
                                    }
                                }}
                                style={{ display: 'flex', alignItems: 'center', gap: '6px', background: '#0d9488', color: '#fff', padding: '8px 14px', borderRadius: '6px', border: 'none', cursor: 'pointer', fontSize: '14px', fontWeight: 500 }}
                            >
                                <DoorOpen size={16} /> Thêm Phòng Bệnh
                            </button>
                        </div>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '24px' }}>
                        {/* Departments & Rooms List */}
                        <div>
                            <h3 style={{ fontSize: '16px', fontWeight: 600, color: '#334155', marginBottom: '12px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <Layers size={18} color="#0284c7" /> Danh sách Khoa & Phòng ({rooms.length} phòng)
                            </h3>
                            <div style={{ maxHeight: '480px', overflowY: 'auto', border: '1px solid #f1f5f9', borderRadius: '8px' }}>
                                {rooms.map(room => {
                                    const isRoomSelected = selectedRoom?.id === room.id;
                                    return (
                                        <div
                                            key={room.id}
                                            onClick={() => selectRoom(room)}
                                            style={{
                                                padding: '12px 16px',
                                                borderBottom: '1px solid #f1f5f9',
                                                cursor: 'pointer',
                                                background: isRoomSelected ? '#f8fafc' : '#fff',
                                                borderLeft: isRoomSelected ? '4px solid #0284c7' : '4px solid transparent',
                                                display: 'flex',
                                                justifyContent: 'space-between',
                                                alignItems: 'center'
                                            }}
                                        >
                                            <div>
                                                <div style={{ fontWeight: 600, color: '#1e293b' }}>{room.roomNumber} - {room.name}</div>
                                                <div style={{ fontSize: '13px', color: '#64748b' }}>{room.departmentName} (Tầng {room.floorNumber})</div>
                                            </div>
                                            <div style={{ textAlign: 'right' }}>
                                                <span style={{ fontSize: '12px', background: '#e0f2fe', color: '#0369a1', padding: '2px 8px', borderRadius: '12px' }}>
                                                    {room.availableBedCount}/{room.bedCount} giường trống
                                                </span>
                                            </div>
                                        </div>
                                    );
                                })}
                                {rooms.length === 0 && (
                                    <div style={{ padding: '32px', textAlign: 'center', color: '#94a3b8' }}>Chưa có phòng bệnh nào. Hãy thêm khoa và phòng mới.</div>
                                )}
                            </div>
                        </div>

                        {/* Bed Management for Selected Room */}
                        <div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                                <h3 style={{ fontSize: '16px', fontWeight: 600, color: '#334155', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <BedDouble size={18} color="#0d9488" />
                                    {selectedRoom ? `Giường bệnh phòng ${selectedRoom.roomNumber}` : 'Chọn phòng để quản lý giường'}
                                </h3>
                                {selectedRoom && (
                                    <button
                                        onClick={() => {
                                            setBedForm({ roomId: selectedRoom.id, bedNumber: '', bedType: 1, dailyRate: 200000, notes: '' });
                                            setBedModalOpen(true);
                                        }}
                                        style={{ background: '#0d9488', color: '#fff', border: 'none', borderRadius: '4px', padding: '4px 10px', fontSize: '13px', cursor: 'pointer', fontWeight: 500 }}
                                    >
                                        + Thêm Giường
                                    </button>
                                )}
                            </div>

                            <div style={{ minHeight: '300px', border: '1px solid #f1f5f9', borderRadius: '8px', padding: '16px', background: '#fafafa' }}>
                                {selectedRoom ? (
                                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '12px' }}>
                                        {beds.map(bed => {
                                            const isAvailable = bed.status === 1;
                                            return (
                                                <div
                                                    key={bed.id}
                                                    style={{
                                                        background: '#fff',
                                                        border: isAvailable ? '1px solid #86efac' : '1px solid #fca5a5',
                                                        borderRadius: '8px',
                                                        padding: '12px',
                                                        boxShadow: '0 1px 2px rgba(0,0,0,0.05)'
                                                    }}
                                                >
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                        <span style={{ fontWeight: 'bold', color: '#1e293b' }}>{bed.bedNumber}</span>
                                                        <span style={{ fontSize: '11px', padding: '2px 6px', borderRadius: '4px', background: isAvailable ? '#dcfce7' : '#fee2e2', color: isAvailable ? '#15803d' : '#b91c1c' }}>
                                                            {bed.statusName}
                                                        </span>
                                                    </div>
                                                    <div style={{ fontSize: '12px', color: '#64748b', marginTop: '6px' }}>{bed.bedTypeName}</div>
                                                    <div style={{ fontSize: '13px', fontWeight: 600, color: '#0284c7', marginTop: '4px' }}>{bed.dailyRate.toLocaleString('vi-VN')} đ/ngày</div>
                                                </div>
                                            );
                                        })}
                                        {beds.length === 0 && (
                                            <div style={{ gridColumn: '1 / -1', textAlign: 'center', padding: '32px', color: '#94a3b8' }}>Chưa có giường bệnh trong phòng này.</div>
                                        )}
                                    </div>
                                ) : (
                                    <div style={{ textAlign: 'center', padding: '60px 20px', color: '#94a3b8' }}>Vui lòng nhấp chọn một phòng bệnh ở cột bên trái để theo dõi và quản lý giường.</div>
                                )}
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* Modal Create Facility */}
            {facilityModalOpen && (
                <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 1000 }}>
                    <div style={{ background: '#fff', borderRadius: '12px', width: '520px', padding: '24px', boxShadow: '0 20px 25px -5px rgba(0,0,0,0.1)' }}>
                        <h2 style={{ fontSize: '18px', fontWeight: 'bold', marginBottom: '16px', color: '#1e293b' }}>Thêm Cơ sở Bệnh viện Mới</h2>
                        <form onSubmit={handleCreateFacility}>
                            <div style={{ marginBottom: '12px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Mã cơ sở *</label>
                                <input required value={facilityForm.code} onChange={e => setFacilityForm({ ...facilityForm, code: e.target.value })} placeholder="VD: FAC-CS04" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                            </div>
                            <div style={{ marginBottom: '12px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Tên bệnh viện / cơ sở *</label>
                                <input required value={facilityForm.name} onChange={e => setFacilityForm({ ...facilityForm, name: e.target.value })} placeholder="VD: Bệnh viện Đa khoa ClinicCare CS4" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginBottom: '12px' }}>
                                <div>
                                    <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Tỉnh/Thành phố *</label>
                                    <input required value={facilityForm.city} onChange={e => setFacilityForm({ ...facilityForm, city: e.target.value })} placeholder="TP. Hồ Chí Minh" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                                </div>
                                <div>
                                    <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Số điện thoại *</label>
                                    <input required value={facilityForm.phone} onChange={e => setFacilityForm({ ...facilityForm, phone: e.target.value })} placeholder="028 1234 5678" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                                </div>
                            </div>
                            <div style={{ marginBottom: '16px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Địa chỉ chi tiết *</label>
                                <input required value={facilityForm.address} onChange={e => setFacilityForm({ ...facilityForm, address: e.target.value })} placeholder="Số 100 Đường ABC, Phường X, Quận Y" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                                <button type="button" onClick={() => setFacilityModalOpen(false)} style={{ padding: '8px 16px', background: '#f1f5f9', border: 'none', borderRadius: '6px', cursor: 'pointer' }}>Hủy</button>
                                <button type="submit" style={{ padding: '8px 16px', background: '#0284c7', color: '#fff', border: 'none', borderRadius: '6px', cursor: 'pointer', fontWeight: 500 }}>Lưu Cơ sở</button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Modal Create Department */}
            {deptModalOpen && (
                <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 1000 }}>
                    <div style={{ background: '#fff', borderRadius: '12px', width: '480px', padding: '24px' }}>
                        <h2 style={{ fontSize: '18px', fontWeight: 'bold', marginBottom: '16px' }}>Thêm Khoa Phòng Mới</h2>
                        <form onSubmit={handleCreateDepartment}>
                            <div style={{ marginBottom: '12px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Mã khoa *</label>
                                <input required value={deptForm.code} onChange={e => setDeptForm({ ...deptForm, code: e.target.value })} placeholder="VD: K-NOI" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                            </div>
                            <div style={{ marginBottom: '12px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Tên khoa *</label>
                                <input required value={deptForm.name} onChange={e => setDeptForm({ ...deptForm, name: e.target.value })} placeholder="VD: Khoa Nội Tiết" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                            </div>
                            <div style={{ marginBottom: '16px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Phân loại khoa</label>
                                <select value={deptForm.departmentType} onChange={e => setDeptForm({ ...deptForm, departmentType: Number(e.target.value) })} style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }}>
                                    <option value={1}>Lâm sàng (Clinical)</option>
                                    <option value={2}>Cận lâm sàng (Paraclinical)</option>
                                    <option value={4}>Dược (Pharmacy)</option>
                                    <option value={5}>Cấp cứu (Emergency)</option>
                                    <option value={7}>Điều trị nội trú (Inpatient)</option>
                                </select>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                                <button type="button" onClick={() => setDeptModalOpen(false)} style={{ padding: '8px 16px', background: '#f1f5f9', border: 'none', borderRadius: '6px', cursor: 'pointer' }}>Hủy</button>
                                <button type="submit" style={{ padding: '8px 16px', background: '#0284c7', color: '#fff', border: 'none', borderRadius: '6px', cursor: 'pointer' }}>Lưu Khoa</button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Modal Create Room */}
            {roomModalOpen && (
                <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 1000 }}>
                    <div style={{ background: '#fff', borderRadius: '12px', width: '480px', padding: '24px' }}>
                        <h2 style={{ fontSize: '18px', fontWeight: 'bold', marginBottom: '16px' }}>Thêm Phòng Mới</h2>
                        <form onSubmit={handleCreateRoom}>
                            <div style={{ marginBottom: '12px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Khoa trực thuộc</label>
                                <select value={roomForm.departmentId} onChange={e => setRoomForm({ ...roomForm, departmentId: Number(e.target.value) })} style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }}>
                                    {departments.map(d => (
                                        <option key={d.id} value={d.id}>{d.name} ({d.code})</option>
                                    ))}
                                </select>
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginBottom: '12px' }}>
                                <div>
                                    <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Số phòng *</label>
                                    <input required value={roomForm.roomNumber} onChange={e => setRoomForm({ ...roomForm, roomNumber: e.target.value })} placeholder="VD: P402" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                                </div>
                                <div>
                                    <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Tầng</label>
                                    <input type="number" value={roomForm.floorNumber} onChange={e => setRoomForm({ ...roomForm, floorNumber: Number(e.target.value) })} style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                                </div>
                            </div>
                            <div style={{ marginBottom: '16px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Tên phòng *</label>
                                <input required value={roomForm.name} onChange={e => setRoomForm({ ...roomForm, name: e.target.value })} placeholder="VD: Phòng Điều Trị Bệnh Tim Mạch 1" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                                <button type="button" onClick={() => setRoomModalOpen(false)} style={{ padding: '8px 16px', background: '#f1f5f9', border: 'none', borderRadius: '6px', cursor: 'pointer' }}>Hủy</button>
                                <button type="submit" style={{ padding: '8px 16px', background: '#0d9488', color: '#fff', border: 'none', borderRadius: '6px', cursor: 'pointer' }}>Lưu Phòng</button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Modal Create Bed */}
            {bedModalOpen && (
                <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 1000 }}>
                    <div style={{ background: '#fff', borderRadius: '12px', width: '420px', padding: '24px' }}>
                        <h2 style={{ fontSize: '18px', fontWeight: 'bold', marginBottom: '16px' }}>Thêm Giường Bệnh</h2>
                        <form onSubmit={handleCreateBed}>
                            <div style={{ marginBottom: '12px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Số/Mã giường *</label>
                                <input required value={bedForm.bedNumber} onChange={e => setBedForm({ ...bedForm, bedNumber: e.target.value })} placeholder="VD: G01" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                            </div>
                            <div style={{ marginBottom: '12px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Giá viện phí / ngày (VNĐ)</label>
                                <input type="number" value={bedForm.dailyRate} onChange={e => setBedForm({ ...bedForm, dailyRate: Number(e.target.value) })} style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                            </div>
                            <div style={{ marginBottom: '16px' }}>
                                <label style={{ display: 'block', fontSize: '13px', fontWeight: 500, marginBottom: '4px' }}>Ghi chú</label>
                                <input value={bedForm.notes} onChange={e => setBedForm({ ...bedForm, notes: e.target.value })} placeholder="VD: Giường gần cửa sổ" style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }} />
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                                <button type="button" onClick={() => setBedModalOpen(false)} style={{ padding: '8px 16px', background: '#f1f5f9', border: 'none', borderRadius: '6px', cursor: 'pointer' }}>Hủy</button>
                                <button type="submit" style={{ padding: '8px 16px', background: '#0d9488', color: '#fff', border: 'none', borderRadius: '6px', cursor: 'pointer' }}>Lưu Giường</button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
