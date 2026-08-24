import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import { MainLayout } from './layouts/MainLayout';
import { PublicLayout } from './layouts/PublicLayout';
import { PublicRoute, ProtectedRoute, RoleRoute } from './routes/guards';
import { Login } from './pages/Login';
import { Register } from './pages/Register';
import { ReceptionistDashboard, DoctorDashboard, AdminDashboard, ForbiddenPage, NotFoundPage } from './pages/Dashboards';
import { PublicLanding } from './pages/public/PublicLanding';
import { BookAppointment } from './pages/patient/BookAppointment';
import { PatientAppointments } from './pages/patient/PatientAppointments';
import { PatientProfile } from './pages/patient/PatientProfile';
import { PatientRevisit } from './pages/patient/PatientRevisit';
import { PatientPrescriptions } from './pages/patient/PatientPrescriptions';
import { PatientAiConsultation } from './pages/patient/PatientAiConsultation';
import { ReceptionAppointments } from './pages/reception/ReceptionAppointments';
import { ReceptionChangeRequests } from './pages/reception/ReceptionChangeRequests';
import { DoctorAppointments } from './pages/doctor/DoctorAppointments';
import { DoctorLeaveRequests } from './pages/doctor/DoctorLeaveRequests';
import { AdminUsers } from './pages/admin/AdminUsers';
import { AdminSpecialties } from './pages/admin/AdminSpecialties';
import { AdminDoctors } from './pages/admin/AdminDoctors';
import { AdminLeaves } from './pages/admin/AdminLeaves';
import { AdminWorkSchedules } from './pages/admin/AdminWorkSchedules';
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
                                
                                <Route element={<PublicRoute />}>
                                    <Route path="/login" element={<Login />} />
                                    <Route path="/register" element={<Register />} />
                                </Route>
                                
                                <Route path="/forbidden" element={<Forbidden />} />

                                {/* Patient Protected Routes (Still in Public Layout) */}
                                <Route element={<ProtectedRoute />}>
                                    <Route element={<RoleRoute roles={['Patient']} />}>
                                        <Route path="/patient" element={<PatientProfile />} /> {/* Redirect patient dashboard to profile */}
                                        <Route path="/patient/profile" element={<PatientProfile />} />
                                        <Route path="/patient/book" element={<BookAppointment />} />
                                        <Route path="/patient/appointments" element={<PatientAppointments />} />
                                        <Route path="/patient/revisit" element={<PatientRevisit />} />
                                        <Route path="/patient/prescriptions" element={<PatientPrescriptions />} />
                                        <Route path="/patient/ai-consultation" element={<PatientAiConsultation />} />
                                    </Route>
                                </Route>
                            </Route>

                            {/* Staff Dashboard Layout (With Sidebar) */}
                            <Route element={<ProtectedRoute />}>
                                <Route element={<MainLayout />}>
                                    {/* Receptionist Routes */}
                                    <Route element={<RoleRoute roles={['Receptionist']} />}>
                                        <Route path="/reception" element={<ReceptionistDashboard />} />
                                        <Route path="/reception/appointments" element={<ReceptionAppointments />} />
                                        <Route path="/reception/change-requests" element={<ReceptionChangeRequests />} />
                                    </Route>

                                    {/* Doctor Routes */}
                                    <Route element={<RoleRoute roles={['Doctor']} />}>
                                        <Route path="/doctor" element={<DoctorDashboard />} />
                                        <Route path="/doctor/appointments" element={<DoctorAppointments />} />
                                        <Route path="/doctor/leave-requests" element={<DoctorLeaveRequests />} />
                                    </Route>

                                    {/* Admin Routes */}
                                    <Route element={<RoleRoute roles={['Admin']} />}>
                                        <Route path="/admin" element={<AdminDashboard />} />
                                        <Route path="/admin/accounts" element={<AdminUsers />} />
                                        <Route path="/admin/specialties" element={<AdminSpecialties />} />
                                        <Route path="/admin/doctors" element={<AdminDoctors />} />
                                        <Route path="/admin/leaves" element={<AdminLeaves />} />
                                        <Route path="/admin/work-schedules" element={<AdminWorkSchedules />} />
                                    </Route>
                                    
                                    {/* Pharmacist Routes */}
                                    <Route element={<RoleRoute roles={['Pharmacist']} />}>
                                        {/* To be implemented */}
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
