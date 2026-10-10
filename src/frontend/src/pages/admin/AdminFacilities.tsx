import React, { useState, useEffect, useRef } from 'react';
import { Alert, Button, Card, Checkbox, Col, Form, Input, InputNumber, Modal, Row, Select, Tabs, Tag, Typography } from 'antd';
import { Plus, Building2, Stethoscope, Phone, Layers, DoorOpen, BedDouble, Trash2, UserPlus } from 'lucide-react';
import { organizationApi, type FacilityDto, type DepartmentDto, type RoomDto, type BedDto, type StaffFacilityAssignmentDto } from '../../api/organizationApi';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader, DataTable, StatusBadge, EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './AdminFacilities.module.css';

interface AssignableUser { id: string; fullName: string; email: string; roles: string[] }
type CreateKind = 'facility' | 'department' | 'room' | 'bed' | 'staff';
const initialFacility = { code: '', name: '', address: '', city: '', phone: '', hospitalLevel: 'Hạng 1', description: '' };
const initialDepartment = { code: '', name: '', departmentType: 1, description: '' };
const initialRoom = { departmentId: 0, roomNumber: '', name: '', roomType: 2, floorNumber: 1, maxCapacity: 4 };
const initialBed = { roomId: 0, bedNumber: '', bedType: 1, dailyRate: 200000, notes: '' };
const initialStaff = { userId: '', role: 'Receptionist', departmentId: 0, isPrimary: true };
const departmentTypes = [
    { value: 1, label: 'Lâm sàng (Clinical)' }, { value: 2, label: 'Cận lâm sàng (Paraclinical)' },
    { value: 3, label: 'Hành chính (Administrative)' }, { value: 4, label: 'Dược (Pharmacy)' },
    { value: 5, label: 'Cấp cứu (Emergency)' }, { value: 6, label: 'Ngoại khoa (Surgical)' },
    { value: 7, label: 'Điều trị nội trú (Inpatient)' },
];
const bedTypes = ['Giường thường', 'Giường điện', 'Giường hồi sức (ICU)', 'Giường nhi', 'Giường cách ly', 'Cáng cấp cứu'].map((label, index) => ({ value: index + 1, label }));
const bedStatuses = ['Trống', 'Đang sử dụng', 'Đang vệ sinh', 'Bảo trì', 'Đã đặt trước'];
const staffRoles = [
    ['Receptionist', 'Lễ tân tiếp đón (Receptionist)'], ['Doctor', 'Bác sĩ khám bệnh (Doctor)'],
    ['Nurse', 'Điều dưỡng viên (Nurse)'], ['Cashier', 'Thu ngân (Cashier)'],
    ['Pharmacist', 'Dược sĩ cấp phát (Pharmacist)'], ['Radiologist', 'Bác sĩ CĐHA (Radiologist)'],
    ['LabTechnician', 'Kỹ thuật viên Xét nghiệm (LabTechnician)'], ['Admin', 'Quản trị viên (Admin)'],
].map(([value, label]) => ({ value, label }));
const errorMessage = (error: unknown, fallback: string) => (error as { message?: string })?.message || fallback;

export const AdminFacilities: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [facilities, setFacilities] = useState<FacilityDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [facilityError, setFacilityError] = useState('');
    const [selectedFacility, setSelectedFacility] = useState<FacilityDto | null>(null);
    const [departments, setDepartments] = useState<DepartmentDto[]>([]);
    const [rooms, setRooms] = useState<RoomDto[]>([]);
    const [structureLoading, setStructureLoading] = useState(false);
    const [structureError, setStructureError] = useState('');
    const [beds, setBeds] = useState<BedDto[]>([]);
    const [selectedRoom, setSelectedRoom] = useState<RoomDto | null>(null);
    const [bedLoading, setBedLoading] = useState(false);
    const [bedError, setBedError] = useState('');
    const [facilityTab, setFacilityTab] = useState('structure');
    const [staffAssignments, setStaffAssignments] = useState<StaffFacilityAssignmentDto[]>([]);
    const [staffLoading, setStaffLoading] = useState(false);
    const [staffError, setStaffError] = useState('');
    const [users, setUsers] = useState<AssignableUser[]>([]);
    const [usersLoading, setUsersLoading] = useState(false);
    const [usersError, setUsersError] = useState('');
    const [facilityModalOpen, setFacilityModalOpen] = useState(false);
    const [deptModalOpen, setDeptModalOpen] = useState(false);
    const [roomModalOpen, setRoomModalOpen] = useState(false);
    const [bedModalOpen, setBedModalOpen] = useState(false);
    const [staffModalOpen, setStaffModalOpen] = useState(false);
    const [facilityForm, setFacilityForm] = useState(initialFacility);
    const [deptForm, setDeptForm] = useState(initialDepartment);
    const [roomForm, setRoomForm] = useState(initialRoom);
    const [bedForm, setBedForm] = useState(initialBed);
    const [staffForm, setStaffForm] = useState(initialStaff);
    const [facilitySubmitting, setFacilitySubmitting] = useState(false);
    const [deptSubmitting, setDeptSubmitting] = useState(false);
    const [roomSubmitting, setRoomSubmitting] = useState(false);
    const [bedSubmitting, setBedSubmitting] = useState(false);
    const [staffSubmitting, setStaffSubmitting] = useState(false);
    // Guard consecutive submit events before React renders the disabled button.
    const submitting = useRef<Record<CreateKind, boolean>>({ facility: false, department: false, room: false, bed: false, staff: false });
    const structureRequest = useRef(0);
    const staffRequest = useRef(0);
    const bedRequest = useRef(0);
    const usersRequest = useRef(0);
    const facilityId = useRef<number | null>(null);
    const selectedRoomId = useRef<number | null>(null);

    const fetchStaffAssignments = async (id: number) => {
        const request = ++staffRequest.current;
        setStaffLoading(true); setStaffError(''); setStaffAssignments([]);
        try {
            const res = await organizationApi.getStaffAssignments(id);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải danh sách phân công nhân viên.');
            if (request === staffRequest.current && facilityId.current === id) setStaffAssignments(res.data);
        } catch (error) {
            if (request === staffRequest.current && facilityId.current === id) setStaffError(errorMessage(error, 'Không thể tải danh sách phân công nhân viên.'));
        } finally {
            if (request === staffRequest.current && facilityId.current === id) setStaffLoading(false);
        }
    };
    const selectFacility = async (fac: FacilityDto) => {
        const request = ++structureRequest.current;
        facilityId.current = fac.id; selectedRoomId.current = null; ++bedRequest.current;
        setSelectedFacility(fac); setSelectedRoom(null); setDepartments([]); setRooms([]); setBeds([]);
        setBedLoading(false); setBedError(''); setStructureLoading(true); setStructureError('');
        void fetchStaffAssignments(fac.id);
        try {
            const [deptRes, roomRes] = await Promise.all([organizationApi.getDepartments(fac.id), organizationApi.getRooms({ facilityId: fac.id })]);
            if (!deptRes.success || !deptRes.data) throw new Error(deptRes.message || 'Không thể tải cấu trúc khoa phòng.');
            if (!roomRes.success || !roomRes.data) throw new Error(roomRes.message || 'Không thể tải danh sách phòng bệnh.');
            if (request === structureRequest.current) { setDepartments(deptRes.data); setRooms(roomRes.data); }
        } catch (error) {
            if (request === structureRequest.current) setStructureError(errorMessage(error, 'Không thể tải cấu trúc khoa phòng.'));
        } finally { if (request === structureRequest.current) setStructureLoading(false); }
    };
    const fetchFacilities = async () => {
        setLoading(true); setFacilityError('');
        try {
            const res = await organizationApi.getFacilities(true);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải danh sách cơ sở bệnh viện.');
            setFacilities(res.data);
            if (res.data.length > 0 && facilityId.current === null) void selectFacility(res.data[0]);
        } catch (error) { setFacilityError(errorMessage(error, 'Không thể tải danh sách cơ sở bệnh viện.')); }
        finally { setLoading(false); }
    };
    const selectRoom = async (room: RoomDto) => {
        const request = ++bedRequest.current;
        selectedRoomId.current = room.id;
        setSelectedRoom(room); setBeds([]); setBedLoading(true); setBedError('');
        try {
            const res = await organizationApi.getBedsByRoom(room.id);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải danh sách giường bệnh.');
            if (request === bedRequest.current) setBeds(res.data);
        } catch (error) { if (request === bedRequest.current) setBedError(errorMessage(error, 'Không thể tải danh sách giường bệnh.')); }
        finally { if (request === bedRequest.current) setBedLoading(false); }
    };
    useEffect(() => {
        const requests = [structureRequest, staffRequest, bedRequest, usersRequest];
        void fetchFacilities();
        return () => { requests.forEach(request => { ++request.current; }); };
    }, []);
    const openStaffModal = async () => {
        const request = ++usersRequest.current;
        setStaffForm(initialStaff); setStaffModalOpen(true); setUsers([]); setUsersError(''); setUsersLoading(true);
        try {
            const res = await axiosClient.get<unknown, ApiResponse<{ items: AssignableUser[] }>>('/admin/users?isActive=true&pageSize=100');
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải danh sách nhân viên.');
            if (request === usersRequest.current) setUsers(res.data.items.filter(user => !(user.roles?.length === 1 && user.roles[0] === 'Patient')));
        } catch (error) { if (request === usersRequest.current) setUsersError(errorMessage(error, 'Không thể tải danh sách nhân viên.')); }
        finally { if (request === usersRequest.current) setUsersLoading(false); }
    };
    const create = async (kind: CreateKind, setBusy: (busy: boolean) => void, operation: () => Promise<ApiResponse<unknown>>, close: () => void, success: string, fallback: string, refresh: () => void) => {
        if (submitting.current[kind]) return;
        submitting.current[kind] = true; setBusy(true);
        try {
            const res = await operation();
            if (!res.success) throw new Error(res.message || fallback);
            showAlert(success, 'Thành công', 'success'); close(); refresh();
        } catch (error) { showAlert(errorMessage(error, fallback), 'Lỗi', 'error'); }
        finally { submitting.current[kind] = false; setBusy(false); }
    };
    const handleCreateFacility = (e: React.FormEvent) => {
        e.preventDefault();
        void create('facility', setFacilitySubmitting, () => organizationApi.createFacility(facilityForm), () => { setFacilityModalOpen(false); setFacilityForm(initialFacility); }, 'Thêm cơ sở y tế thành công!', 'Không thể tạo cơ sở.', () => { void fetchFacilities(); });
    };
    const handleCreateDepartment = (e: React.FormEvent) => {
        e.preventDefault(); if (!selectedFacility) return;
        const fac = selectedFacility;
        void create('department', setDeptSubmitting, () => organizationApi.createDepartment({ facilityId: fac.id, ...deptForm }), () => { setDeptModalOpen(false); setDeptForm(initialDepartment); }, 'Thêm khoa phòng thành công!', 'Không thể tạo khoa phòng.', () => { if (facilityId.current === fac.id) void selectFacility(fac); });
    };
    const handleCreateRoom = (e: React.FormEvent) => {
        e.preventDefault(); if (!selectedFacility) return;
        const fac = selectedFacility;
        void create('room', setRoomSubmitting, () => organizationApi.createRoom(roomForm), () => setRoomModalOpen(false), 'Thêm phòng thành công!', 'Không thể tạo phòng.', () => { if (facilityId.current === fac.id) void selectFacility(fac); });
    };
    const handleCreateBed = (e: React.FormEvent) => {
        e.preventDefault(); if (!selectedRoom) return;
        const room = selectedRoom;
        void create('bed', setBedSubmitting, () => organizationApi.createBed({ ...bedForm, roomId: room.id }), () => setBedModalOpen(false), 'Thêm giường bệnh thành công!', 'Không thể tạo giường bệnh.', () => { if (selectedRoomId.current === room.id) void selectRoom(room); });
    };
    const handleCreateStaffAssignment = (e: React.FormEvent) => {
        e.preventDefault(); if (!selectedFacility || usersLoading || usersError) return;
        if (!staffForm.userId) { showAlert('Vui lòng chọn nhân viên.', 'Thông báo', 'warning'); return; }
        const id = selectedFacility.id;
        void create('staff', setStaffSubmitting, () => organizationApi.createStaffAssignment({ userId: staffForm.userId, facilityId: id, role: staffForm.role, departmentId: staffForm.departmentId > 0 ? staffForm.departmentId : undefined, isPrimary: staffForm.isPrimary }), () => { setStaffModalOpen(false); setStaffForm(initialStaff); }, 'Phân công nhân viên vào cơ sở thành công!', 'Không thể tạo phân công nhân viên.', () => { if (facilityId.current === id) void fetchStaffAssignments(id); });
    };
    const handleDeleteStaffAssignment = (id: number) => {
        const assignedFacilityId = selectedFacility?.id;
        showConfirm('Bạn có chắc chắn muốn hủy phân công nhân sự này?', async () => {
            try {
                const res = await organizationApi.deleteStaffAssignment(id);
                if (!res.success) throw new Error(res.message || 'Không thể hủy phân công.');
                showAlert('Đã hủy phân công nhân sự.', 'Thành công', 'success');
                if (assignedFacilityId && facilityId.current === assignedFacilityId) void fetchStaffAssignments(assignedFacilityId);
            } catch (error) { showAlert(errorMessage(error, 'Không thể hủy phân công.'), 'Lỗi', 'error'); }
        });
    };
    const modalProps = (busy: boolean) => ({ footer: null, maskClosable: !busy, closable: !busy, keyboard: !busy, destroyOnHidden: true });
    const footer = (busy: boolean, close: () => void, label: string, disabled = false) => <div className={styles.formActions}><Button disabled={busy} onClick={close}>Hủy</Button><Button type="primary" htmlType="submit" disabled={busy || disabled}>{busy ? 'Đang lưu...' : label}</Button></div>;
    const departmentOptions = departments.map(d => ({ value: d.id, label: d.name + ' (' + d.code + ')' }));

    return <div className={styles.page}>
        <PageHeader title="Quản trị Mạng lưới Bệnh viện & Cơ sở" subtitle="Quản lý cây tổ chức: Cơ sở ➔ Tòa nhà ➔ Khoa phòng ➔ Phòng bệnh ➔ Giường bệnh" actions={<Button type="primary" icon={<Plus size={18} />} onClick={() => setFacilityModalOpen(true)}>Thêm Cơ sở Mới</Button>} />
        {loading ? <LoadingState message="Đang tải dữ liệu mạng lưới cơ sở..." /> : facilityError ? <InlineError message={facilityError} onRetry={fetchFacilities} /> : facilities.length === 0 ? <EmptyState title="Chưa có cơ sở bệnh viện nào." /> : <>
            <div className={styles.facilityGrid}>{facilities.map(fac => <Card key={fac.id} role="button" tabIndex={0} aria-pressed={selectedFacility?.id === fac.id} className={[styles.facilityCard, selectedFacility?.id === fac.id ? styles.selectedCard : ''].join(' ')} onClick={() => { void selectFacility(fac); }} onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); void selectFacility(fac); } }}>
                <div className={styles.sectionHeading}><Tag color="processing">{fac.code}</Tag><StatusBadge status={fac.isActive ? 'Approved' : 'Rejected'} label={fac.isActive ? 'Hoạt động' : 'Tạm dừng'} /></div>
                <h3>{fac.name}</h3><Typography.Text type="secondary">{fac.address}, {fac.city}</Typography.Text>
                <div className={styles.facilityFacts}><span><Building2 size={16} /> {fac.buildingCount} Tòa nhà</span><span><Stethoscope size={16} /> {fac.departmentCount} Khoa/Phòng</span><span><Phone size={16} /> {fac.phone}</span></div>
            </Card>)}</div>
            {selectedFacility && <Card><Tabs activeKey={facilityTab} onChange={setFacilityTab} items={[
                { key: 'structure', label: 'Cấu trúc Khoa & Phòng (' + rooms.length + ' phòng)', children: <>
                    <div className={styles.sectionHeading}><div><h2>{'Cấu trúc Khoa phòng & Buồng Giường: ' + selectedFacility.name}</h2><Typography.Text type="secondary">Phân cấp chi tiết các đơn vị điều trị trực thuộc</Typography.Text></div><div className={styles.actions}>
                        <Button type="primary" icon={<Plus size={16} />} disabled={structureLoading} onClick={() => { setDeptForm(initialDepartment); setDeptModalOpen(true); }}>Thêm Khoa Phòng</Button>
                        <Button icon={<DoorOpen size={16} />} disabled={structureLoading} onClick={() => { if (departments.length) { setRoomForm({ ...initialRoom, departmentId: departments[0].id }); setRoomModalOpen(true); } else showAlert('Cần tạo ít nhất 1 khoa phòng trước khi tạo phòng.', 'Thông báo', 'warning'); }}>Thêm Phòng Bệnh</Button>
                    </div></div>
                    {structureLoading ? <LoadingState message="Đang tải cấu trúc khoa phòng..." /> : structureError ? <InlineError message={structureError} onRetry={() => { void selectFacility(selectedFacility); }} /> : <Row className={styles.structureRow}>
                        <Col xs={24} lg={12} className={styles.structureColumn}><h3 className={styles.iconHeading}><Layers size={18} /> Danh sách Khoa & Phòng ({rooms.length} phòng)</h3>
                            {rooms.length === 0 ? <EmptyState title="Chưa có phòng bệnh nào. Hãy thêm khoa và phòng mới." /> : <div className={styles.roomList}>{rooms.map(room => <button type="button" key={room.id} aria-pressed={selectedRoom?.id === room.id} className={[styles.roomButton, selectedRoom?.id === room.id ? styles.selectedRoom : ''].join(' ')} onClick={() => { void selectRoom(room); }}><span><strong>{room.roomNumber} - {room.name}</strong><span className={styles.secondary}>{room.departmentName} (Tầng {room.floorNumber})</span></span><Tag color="processing">{room.availableBedCount}/{room.bedCount} giường trống</Tag></button>)}</div>}
                        </Col>
                        <Col xs={24} lg={12} className={styles.structureColumn}><div className={styles.sectionHeading}><h3 className={styles.iconHeading}><BedDouble size={18} /> {selectedRoom ? 'Giường bệnh phòng ' + selectedRoom.roomNumber : 'Chọn phòng để quản lý giường'}</h3>{selectedRoom && <Button onClick={() => { setBedForm({ ...initialBed, roomId: selectedRoom.id }); setBedModalOpen(true); }}>+ Thêm Giường</Button>}</div>
                            {!selectedRoom ? <EmptyState title="Vui lòng nhấp chọn một phòng bệnh ở cột bên trái để theo dõi và quản lý giường." /> : bedLoading ? <LoadingState message="Đang tải danh sách giường bệnh..." /> : bedError ? <InlineError message={bedError} onRetry={() => { void selectRoom(selectedRoom); }} /> : beds.length === 0 ? <EmptyState title="Chưa có giường bệnh trong phòng này." /> : <div className={styles.bedGrid}>{beds.map(bed => <Card key={bed.id} size="small"><div className={styles.sectionHeading}><strong>{bed.bedNumber}</strong><Tag color={bed.status === 1 ? 'success' : bed.status === 2 ? 'error' : 'warning'}>{bedStatuses[bed.status - 1] || 'Chưa cập nhật'}</Tag></div><div className={styles.secondary}>{bedTypes.find(type => type.value === bed.bedType)?.label || 'Chưa cập nhật'}</div><strong className={styles.bedPrice}>{bed.dailyRate.toLocaleString('vi-VN')} đ/ngày</strong></Card>)}</div>}
                        </Col>
                    </Row>}
                </> },
                { key: 'staff', label: 'Phân công nhân sự (' + staffAssignments.length + ')', children: <>
                    <div className={styles.sectionHeading}><div><h2>{'Danh sách Nhân viên tại cơ sở: ' + selectedFacility.name}</h2><Typography.Text type="secondary">Phân công cán bộ y tế, bác sĩ, điều dưỡng, lễ tân và thu ngân làm việc tại cơ sở</Typography.Text></div><Button type="primary" icon={<UserPlus size={16} />} onClick={() => { void openStaffModal(); }}>Phân công nhân viên mới</Button></div>
                    {staffLoading ? <LoadingState message="Đang tải danh sách nhân viên..." /> : staffError ? <InlineError message={staffError} onRetry={() => { void fetchStaffAssignments(selectedFacility.id); }} /> : staffAssignments.length === 0 ? <EmptyState title="Chưa có nhân viên nào được phân công tại cơ sở này." /> : <DataTable data={staffAssignments} keyExtractor={s => s.id} columns={[
                        { header: 'Nhân viên', accessor: s => <><strong>{s.fullName || s.userName || 'Chưa cập nhật'}</strong><span className={styles.secondary}>ID: {s.userId}</span></> },
                        { header: 'Email', accessor: s => s.email || '-' }, { header: 'Vai trò', accessor: s => <Tag>{s.role}</Tag> },
                        { header: 'Khoa', accessor: s => s.departmentName || 'Toàn cơ sở' },
                        { header: 'Cơ sở', accessor: s => <Tag color={s.isPrimary ? 'success' : 'default'}>{s.isPrimary ? 'Cơ sở chính' : 'Cơ sở phụ'}</Tag> },
                        { header: 'Trạng thái', accessor: s => <StatusBadge status={s.isActive ? 'Approved' : 'Rejected'} label={s.isActive ? 'Hoạt động' : 'Tạm dừng'} /> },
                        { header: 'Ngày phân công', accessor: s => new Date(s.assignedAtUtc).toLocaleDateString('vi-VN') },
                        { header: 'Thao tác', align: 'center', accessor: s => <Button type="text" danger aria-label="Hủy phân công" title="Hủy phân công" icon={<Trash2 size={16} />} onClick={() => handleDeleteStaffAssignment(s.id)} /> },
                    ]} />}
                </> },
            ]} /></Card>}
        </>}
        <Modal open={facilityModalOpen} title="Thêm Cơ sở Bệnh viện Mới" {...modalProps(facilitySubmitting)} onCancel={() => { if (!submitting.current.facility) setFacilityModalOpen(false); }}>
            <Form layout="vertical" onSubmitCapture={handleCreateFacility}>
                <Form.Item label="Mã cơ sở *" htmlFor="facilityCode"><Input id="facilityCode" required disabled={facilitySubmitting} value={facilityForm.code} onChange={e => setFacilityForm({ ...facilityForm, code: e.target.value })} placeholder="VD: FAC-CS04" /></Form.Item>
                <Form.Item label="Tên bệnh viện / cơ sở *" htmlFor="facilityName"><Input id="facilityName" required disabled={facilitySubmitting} value={facilityForm.name} onChange={e => setFacilityForm({ ...facilityForm, name: e.target.value })} placeholder="VD: Bệnh viện Đa khoa ClinicCare CS4" /></Form.Item>
                <div className={styles.formGrid}><Form.Item label="Tỉnh/Thành phố *" htmlFor="facilityCity"><Input id="facilityCity" required disabled={facilitySubmitting} value={facilityForm.city} onChange={e => setFacilityForm({ ...facilityForm, city: e.target.value })} placeholder="TP. Hồ Chí Minh" /></Form.Item><Form.Item label="Số điện thoại *" htmlFor="facilityPhone"><Input id="facilityPhone" required disabled={facilitySubmitting} value={facilityForm.phone} onChange={e => setFacilityForm({ ...facilityForm, phone: e.target.value })} placeholder="028 1234 5678" /></Form.Item></div>
                <Form.Item label="Địa chỉ chi tiết *" htmlFor="facilityAddress"><Input id="facilityAddress" required disabled={facilitySubmitting} value={facilityForm.address} onChange={e => setFacilityForm({ ...facilityForm, address: e.target.value })} placeholder="Số 100 Đường ABC, Phường X, Quận Y" /></Form.Item>
                {footer(facilitySubmitting, () => setFacilityModalOpen(false), 'Lưu Cơ sở')}
            </Form>
        </Modal>
        <Modal open={deptModalOpen} title="Thêm Khoa Phòng Mới" {...modalProps(deptSubmitting)} onCancel={() => { if (!submitting.current.department) setDeptModalOpen(false); }}>
            <Form layout="vertical" onSubmitCapture={handleCreateDepartment}>
                <Form.Item label="Mã khoa *" htmlFor="departmentCode"><Input id="departmentCode" required disabled={deptSubmitting} value={deptForm.code} onChange={e => setDeptForm({ ...deptForm, code: e.target.value })} placeholder="VD: K-NOI" /></Form.Item>
                <Form.Item label="Tên khoa *" htmlFor="departmentName"><Input id="departmentName" required disabled={deptSubmitting} value={deptForm.name} onChange={e => setDeptForm({ ...deptForm, name: e.target.value })} placeholder="VD: Khoa Nội Tiết" /></Form.Item>
                <Form.Item label="Phân loại khoa" htmlFor="departmentType"><Select id="departmentType" aria-label="Phân loại khoa" disabled={deptSubmitting} value={deptForm.departmentType} onChange={value => setDeptForm({ ...deptForm, departmentType: value })} options={departmentTypes} /></Form.Item>
                {footer(deptSubmitting, () => setDeptModalOpen(false), 'Lưu Khoa')}
            </Form>
        </Modal>
        <Modal open={roomModalOpen} title="Thêm Phòng Mới" {...modalProps(roomSubmitting)} onCancel={() => { if (!submitting.current.room) setRoomModalOpen(false); }}>
            <Form layout="vertical" onSubmitCapture={handleCreateRoom}>
                <Form.Item label="Khoa trực thuộc" htmlFor="roomDepartment"><Select id="roomDepartment" aria-label="Khoa trực thuộc" disabled={roomSubmitting} value={roomForm.departmentId} onChange={value => setRoomForm({ ...roomForm, departmentId: value })} options={departmentOptions} /></Form.Item>
                <div className={styles.formGrid}><Form.Item label="Số phòng *" htmlFor="roomNumber"><Input id="roomNumber" required disabled={roomSubmitting} value={roomForm.roomNumber} onChange={e => setRoomForm({ ...roomForm, roomNumber: e.target.value })} placeholder="VD: P402" /></Form.Item><Form.Item label="Tầng" htmlFor="roomFloor"><InputNumber id="roomFloor" min={1} disabled={roomSubmitting} value={roomForm.floorNumber} onChange={value => setRoomForm({ ...roomForm, floorNumber: value ?? 1 })} className={styles.numberInput} /></Form.Item></div>
                <Form.Item label="Tên phòng *" htmlFor="roomName"><Input id="roomName" required disabled={roomSubmitting} value={roomForm.name} onChange={e => setRoomForm({ ...roomForm, name: e.target.value })} placeholder="VD: Phòng Điều Trị Bệnh Tim Mạch 1" /></Form.Item>
                <Form.Item label="Sức chứa tối đa (giường)" htmlFor="roomCapacity"><InputNumber id="roomCapacity" min={1} precision={0} disabled={roomSubmitting} value={roomForm.maxCapacity} onChange={value => setRoomForm({ ...roomForm, maxCapacity: value ?? 4 })} className={styles.numberInput} /></Form.Item>
                {footer(roomSubmitting, () => setRoomModalOpen(false), 'Lưu Phòng')}
            </Form>
        </Modal>
        <Modal open={bedModalOpen} title="Thêm Giường Bệnh" {...modalProps(bedSubmitting)} onCancel={() => { if (!submitting.current.bed) setBedModalOpen(false); }}>
            <Form layout="vertical" onSubmitCapture={handleCreateBed}>
                <Form.Item label="Số/Mã giường *" htmlFor="bedNumber"><Input id="bedNumber" required disabled={bedSubmitting} value={bedForm.bedNumber} onChange={e => setBedForm({ ...bedForm, bedNumber: e.target.value })} placeholder="VD: G01" /></Form.Item>
                <Form.Item label="Loại giường" htmlFor="bedType"><Select id="bedType" aria-label="Loại giường" disabled={bedSubmitting} value={bedForm.bedType} onChange={value => setBedForm({ ...bedForm, bedType: value })} options={bedTypes} /></Form.Item>
                <Form.Item label="Giá viện phí / ngày (VNĐ)" htmlFor="bedRate"><InputNumber id="bedRate" min={0} step={50000} disabled={bedSubmitting} value={bedForm.dailyRate} onChange={value => setBedForm({ ...bedForm, dailyRate: value ?? 0 })} className={styles.numberInput} /></Form.Item>
                <Form.Item label="Ghi chú" htmlFor="bedNotes"><Input id="bedNotes" disabled={bedSubmitting} value={bedForm.notes} onChange={e => setBedForm({ ...bedForm, notes: e.target.value })} placeholder="VD: Giường gần cửa sổ" /></Form.Item>
                {footer(bedSubmitting, () => setBedModalOpen(false), 'Lưu Giường')}
            </Form>
        </Modal>
        <Modal open={staffModalOpen} title={'Phân công nhân viên vào ' + (selectedFacility?.name || '')} {...modalProps(staffSubmitting)} onCancel={() => { if (!submitting.current.staff) { setStaffModalOpen(false); ++usersRequest.current; } }}>
            <Form layout="vertical" onSubmitCapture={handleCreateStaffAssignment}>
                {usersLoading ? <LoadingState message="Đang tải danh sách nhân viên..." /> : usersError && <Alert type="error" title={usersError} showIcon />}
                <Form.Item label="Nhân viên *" htmlFor="assignedUser"><Select id="assignedUser" aria-label="Nhân viên *" showSearch optionFilterProp="label" disabled={usersLoading || !!usersError || staffSubmitting} value={staffForm.userId || undefined} onChange={value => setStaffForm({ ...staffForm, userId: value })} options={users.map(user => ({ value: user.id, label: user.fullName + ' (' + user.email + ')' }))} /></Form.Item>
                <Form.Item label="Vai trò nhiệm vụ tại cơ sở *" htmlFor="assignmentRole"><Select id="assignmentRole" aria-label="Vai trò nhiệm vụ tại cơ sở *" disabled={staffSubmitting} value={staffForm.role} onChange={value => setStaffForm({ ...staffForm, role: value })} options={staffRoles} /></Form.Item>
                <Form.Item label="Khoa phòng trực thuộc (nếu có)" htmlFor="assignmentDepartment"><Select id="assignmentDepartment" aria-label="Khoa phòng trực thuộc (nếu có)" disabled={staffSubmitting} value={staffForm.departmentId} onChange={value => setStaffForm({ ...staffForm, departmentId: value })} options={[{ value: 0, label: '-- Toàn bộ cơ sở / Không gán khoa cụ thể --' }, ...departmentOptions]} /></Form.Item>
                <Form.Item htmlFor="isPrimaryAssignment"><Checkbox id="isPrimaryAssignment" disabled={staffSubmitting} checked={staffForm.isPrimary} onChange={e => setStaffForm({ ...staffForm, isPrimary: e.target.checked })}>Đặt làm Cơ sở công tác chính (Primary Facility)</Checkbox></Form.Item>
                {footer(staffSubmitting, () => { setStaffModalOpen(false); ++usersRequest.current; }, 'Lưu Phân công', usersLoading || !!usersError)}
            </Form>
        </Modal>
    </div>;
};
