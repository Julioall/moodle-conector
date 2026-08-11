import { createAppClient } from '../../integrations/http-client';

export type StudentGrade = { itemId: string; name: string; grade?: number; maximum?: number; percentage?: number; feedback?: string; readOnly: boolean };
export type StudentCourse = { connectionRef: string; courseId: string; name: string; url?: string; enrollmentStatus: string; progress?: number; lastCourseAccessAt?: string; grades: StudentGrade[] };
export type Student = { studentRef: { connectionRef: string; studentId: string }; connectionRef: string; studentId: string; name: string; email?: string; suspended?: boolean; firstAccessAt?: string; lastAccessAt?: string; lastCourseAccessAt?: string; risk: string; riskFactors: string[]; courses: StudentCourse[]; moodleUrl?: string };
export type StudentList = { data: Student[]; meta: { page: number; pageSize: number; returned: number; total?: number; hasMore: boolean; generatedAt: string; connectionRef?: string } };
export type StudentResponse = { data: Student; meta: { generatedAt: string; connectionRef?: string } };

export const createStudentsGateway = (client = createAppClient()) => ({
  byCourse: (connectionRef: string, courseId: string, page = 1, pageSize = 20) => client.get<StudentList>(`/api/courses/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/students?page=${page}&pageSize=${pageSize}`),
  get: (connectionRef: string, courseId: string, studentId: string) => client.get<StudentResponse>(`/api/courses/${encodeURIComponent(connectionRef)}/${encodeURIComponent(courseId)}/students/${encodeURIComponent(studentId)}`),
});
export const studentsGateway = createStudentsGateway();

