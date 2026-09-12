import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import { MainLayout } from './layouts/MainLayout';
import { PublicLayout } from './layouts/PublicLayout';
import { PublicRoute, ProtectedRoute, RoleRoute } from './routes/guards';
import { Login } from './pages/Login';
import { Register } from './pages/Register';
import { ReceptionistDashboard, AdminDashboard, ForbiddenPage, NotFoundPage } from './pages/Dashboards';
import { PublicLanding } from './pages/public/PublicLanding';
import { SpecialtiesList } from './pages/public/SpecialtiesList';
import { SpecialtyDetail } from './pages/public/SpecialtyDetail';
import { DoctorsList } from './pages/public/DoctorsList';
import { DoctorDetail } from './pages/public/DoctorDetail';
import { HealthPackagesList } from './pages/public/HealthPackagesList';
import { HealthPackageDetail } from './pages/public/HealthPackageDetail';
import { LocationsList } from './pages/public/LocationsList';
import { SearchResults } from './pages/public/SearchResults';
import { BookAppointment } from './pages/patient/BookAppointment';
import { PackageRegistration } from './pages/patient/PackageRegistration';
import { PatientPackageRegistrations } from './pages/patient/PatientPackageRegistrations';
import { PatientAppointments } from './pages/patient/PatientAppointments';
import { PatientProfile } from './pages/patient/PatientProfile';
import { PatientRevisit } from './pages/patient/PatientRevisit';
import { PatientPrescriptions } from './pages/patient/PatientPrescriptions';
import { PatientInvoices } from './pages/patient/PatientInvoices';
import { PatientAiConsultation } from './pages/patient/PatientAiConsultation';
import { ReceptionAppointments } from './pages/reception/ReceptionAppointments';
import { ReceptionPackageRegistrations } from './pages/reception/ReceptionPackageRegistrations';
import { ReceptionChangeRequests } from './pages/reception/ReceptionChangeRequests';
import { ReceptionBilling } from './pages/reception/ReceptionBilling';
import { DoctorDashboard } from './pages/doctor/DoctorDashboard';
import { DoctorQueue } from './pages/doctor/DoctorQueue';
import { DoctorSchedule } from './pages/doctor/DoctorSchedule';
import { DoctorAppointments } from './pages/doctor/DoctorAppointments';
import { DoctorAppointmentDetail } from './pages/doctor/DoctorAppointmentDetail';
import { DoctorExaminationWorkspace } from './pages/doctor/DoctorExaminationWorkspace';
import { DoctorLeaveRequests } from './pages/doctor/DoctorLeaveRequests';
import { AdminUsers } from './pages/admin/AdminUsers';
import { AdminSpecialties } from './pages/admin/AdminSpecialties';
import { AdminDoctors } from './pages/admin/AdminDoctors';
import { AdminLeaves } from './pages/admin/AdminLeaves';
import { AdminWorkSchedules } from './pages/admin/AdminWorkSchedules';
import { AdminHealthPackages } from './pages/admin/AdminHealthPackages';
import { AdminMedicines } from './pages/admin/AdminMedicines';
import { AdminAuditLogs } from './pages/admin/AdminAuditLogs';
import { AdminBilling } from './pages/admin/AdminBilling';
import { PharmacyDashboard } from './pages/pharmacy/PharmacyDashboard';
import { PharmacyPrescriptions } from './pages/pharmacy/PharmacyPrescriptions';
import { PharmacyInventory } from './pages/pharmacy/PharmacyInventory';
import { NotificationsPage } from './pages/common/NotificationsPage';
import { DiagnosticOrderPrint } from './pages/doctor/DiagnosticOrderPrint';
import { TechnicianDashboard } from './pages/technician/TechnicianDashboard';
import { TechnicianOrderDetail } from './pages/technician/TechnicianOrderDetail';
import { PatientDiagnosticResults } from './pages/patient/PatientDiagnosticResults';
import { ChatProvider } from './contexts/ChatContext';
import { DialogProvider } from './contexts/DialogContext';
import { MedicalChatWidget } from './components/MedicalChatWidget';
import { Forbidden } from './pages/Forbidden';

function App() {
    return (
        <DialogProvider>
            <AuthProvider>
                <ChatProvider>
                    <Router>
                        <MedicalChatWidget />
                        <Routes>
                            {/* Public Website Layout (No Sidebar) */}
                            <Route element={<PublicLayout />}>
                                <Route path="/" element={<PublicLanding />} />
                                <Route path="/specialties" element={<SpecialtiesList />} />
                                <Route path="/specialties/:id" element={<SpecialtyDetail />} />
                                <Route path="/doctors" element={<DoctorsList />} />
                                <Route path="/doctors/:id" element={<DoctorDetail />} />
                                <Route path="/health-packages" element={<HealthPackagesList />} />
                                <Route path="/health-packages/:id" element={<HealthPackageDetail />} />
                                <Route path="/locations" element={<LocationsList />} />
                                <Route path="/search" element={<SearchResults />} />
                                
                                <Route element={<PublicRoute />}>
                                    <Route path="/login" element={<Login />} />
                                    <Route path="/register" element={<Register />} />
                                </Route>
                                
                                <Route path="/forbidden" element={<Forbidden />} />
                                <Route path="/403" element={<Forbidden />} />

                                {/* Patient Protected Routes (Still in Public Layout) */}
                                <Route element={<ProtectedRoute />}>
                                    <Route element={<RoleRoute roles={['Patient']} />}>
                                        <Route path="/patient" element={<PatientProfile />} />
                                        <Route path="/patient/profile" element={<PatientProfile />} />
                                        <Route path="/patient/book" element={<BookAppointment />} />
                                        <Route path="/patient/health-packages/:id/register" element={<PackageRegistration />} />
                                        <Route path="/patient/health-package-registrations" element={<PatientPackageRegistrations />} />
                                        <Route path="/patient/appointments" element={<PatientAppointments />} />
                                        <Route path="/patient/revisit" element={<PatientRevisit />} />
                                        <Route path="/patient/prescriptions" element={<PatientPrescriptions />} />
                                        <Route path="/patient/diagnostic-results" element={<PatientDiagnosticResults />} />
                                        <Route path="/patient/invoices" element={<PatientInvoices />} />
                                        <Route path="/patient/ai-consultation" element={<PatientAiConsultation />} />
                                        <Route path="/patient/notifications" element={<NotificationsPage />} />
                                    </Route>
                                </Route>
                            </Route>

                            {/* Staff Dashboard Layout (With Sidebar) */}
                            <Route element={<ProtectedRoute />}>
                                <Route element={<MainLayout />}>
                                    <Route path="/notifications" element={<NotificationsPage />} />
                                    {/* Receptionist Routes */}
                                    <Route element={<RoleRoute roles={['Receptionist']} />}>
                                        <Route path="/reception" element={<ReceptionistDashboard />} />
                                        <Route path="/reception/appointments" element={<ReceptionAppointments />} />
                                        <Route path="/reception/package-registrations" element={<ReceptionPackageRegistrations />} />
                                        <Route path="/reception/billing" element={<ReceptionBilling />} />
                                        <Route path="/reception/change-requests" element={<ReceptionChangeRequests />} />
                                    </Route>

                                    {/* Doctor Routes */}
                                    <Route element={<RoleRoute roles={['Doctor']} />}>
                                        <Route path="/doctor" element={<DoctorDashboard />} />
                                        <Route path="/doctor/queue" element={<DoctorQueue />} />
                                        <Route path="/doctor/schedule" element={<DoctorSchedule />} />
                                        <Route path="/doctor/appointments" element={<DoctorAppointments />} />
                                        <Route path="/doctor/appointments/:id" element={<DoctorAppointmentDetail />} />
                                        <Route path="/doctor/appointments/:id/examination" element={<DoctorExaminationWorkspace />} />
                                        <Route path="/doctor/diagnostic-orders/:id/print" element={<DiagnosticOrderPrint />} />
                                        <Route path="/doctor/leave-requests" element={<DoctorLeaveRequests />} />
                                    </Route>

                                    {/* Admin Routes */}
                                    <Route element={<RoleRoute roles={['Admin']} />}>
                                        <Route path="/admin" element={<AdminDashboard />} />
                                        <Route path="/admin/billing" element={<AdminBilling />} />
                                        <Route path="/admin/accounts" element={<AdminUsers />} />
                                        <Route path="/admin/specialties" element={<AdminSpecialties />} />
                                        <Route path="/admin/doctors" element={<AdminDoctors />} />
                                        <Route path="/admin/packages" element={<AdminHealthPackages />} />
                                        <Route path="/admin/medicines" element={<AdminMedicines />} />
                                        <Route path="/admin/leaves" element={<AdminLeaves />} />
                                        <Route path="/admin/work-schedules" element={<AdminWorkSchedules />} />
                                        <Route path="/admin/audit-logs" element={<AdminAuditLogs />} />
                                    </Route>
                                    
                                    {/* Pharmacist Routes */}
                                    <Route element={<RoleRoute roles={['Pharmacist']} />}>
                                        <Route path="/pharmacy" element={<PharmacyDashboard />} />
                                        <Route path="/pharmacy/prescriptions" element={<PharmacyPrescriptions />} />
                                        <Route path="/pharmacy/medicines" element={<AdminMedicines />} />
                                        <Route path="/pharmacy/inventory" element={<PharmacyInventory />} />
                                    </Route>

                                    {/* Diagnostic Technician Routes */}
                                    <Route element={<RoleRoute roles={['DiagnosticTechnician']} />}>
                                        <Route path="/diagnostics" element={<TechnicianDashboard />} />
                                        <Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} />
                                    </Route>
                                </Route>
                            </Route>

                            <Route path="/403" element={<ForbiddenPage />} />

                            {/* 404 Not Found */}
                            <Route path="*" element={<NotFoundPage />} />
                        </Routes>
                    </Router>
                </ChatProvider>
            </AuthProvider>
        </DialogProvider>
    );
}

export default App;
