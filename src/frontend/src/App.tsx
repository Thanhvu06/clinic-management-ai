import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import { MainLayout } from './layouts/MainLayout';
import { PublicRoute, ProtectedRoute, RoleRoute } from './routes/guards';
import { Login } from './pages/Login';
import { Register } from './pages/Register';
import { PatientDashboard, ReceptionistDashboard, DoctorDashboard, AdminDashboard, ForbiddenPage, NotFoundPage } from './pages/Dashboards';
import { BookAppointment } from './pages/patient/BookAppointment';
import { PatientAppointments } from './pages/patient/PatientAppointments';
import { PatientProfile } from './pages/patient/PatientProfile';
import { PatientRevisit } from './pages/patient/PatientRevisit';
import { ReceptionAppointments } from './pages/reception/ReceptionAppointments';
import { ReceptionChangeRequests } from './pages/reception/ReceptionChangeRequests';
import { DoctorAppointments } from './pages/doctor/DoctorAppointments';
import { DoctorLeaveRequests } from './pages/doctor/DoctorLeaveRequests';
import { AdminUsers } from './pages/admin/AdminUsers';
import { AdminSpecialties } from './pages/admin/AdminSpecialties';
import { AdminDoctors } from './pages/admin/AdminDoctors';
import { AdminLeaves } from './pages/admin/AdminLeaves';
import { AdminWorkSchedules } from './pages/admin/AdminWorkSchedules';
import { Forbidden } from './pages/Forbidden';

function App() {
    return (
        <AuthProvider>
            <Router>
                <Routes>
                    <Route element={<PublicRoute />}>
                        <Route path="/login" element={<Login />} />
                        <Route path="/register" element={<Register />} />
                    </Route>
                    <Route path="/forbidden" element={<Forbidden />} />

                    <Route element={<ProtectedRoute />}>
                        <Route element={<MainLayout />}>
                            {/* Patient Routes */}
                            <Route element={<RoleRoute roles={['Patient']} />}>
                                <Route path="/patient" element={<PatientDashboard />} />
                                <Route path="/patient/profile" element={<PatientProfile />} />
                                <Route path="/patient/book" element={<BookAppointment />} />
                                <Route path="/patient/appointments" element={<PatientAppointments />} />
                                <Route path="/patient/revisit" element={<PatientRevisit />} />
                            </Route>

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
                        </Route>
                    </Route>

                    <Route path="/403" element={<ForbiddenPage />} />
                    
                    {/* Redirect root to login */}
                    <Route path="/" element={<Navigate to="/login" replace />} />
                    
                    {/* 404 Not Found */}
                    <Route path="*" element={<NotFoundPage />} />
                </Routes>
            </Router>
        </AuthProvider>
    );
}

export default App;
