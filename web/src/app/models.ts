export type Role = 'learner' | 'instructor';
export type BookingStatus = 'confirmed' | 'waitlisted' | 'cancelled';

export interface DemoUser {
  userId: string;
  tenantId: string;
  email: string;
  displayName: string;
  role: Role;
  tenantName: string;
}

export interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  user: DemoUser;
}

export interface LabEvent {
  eventId: string;
  title: string;
  labTemplate: string;
  startsAt: string;
  endsAt: string;
  capacity: number;
  confirmed: number;
  waitlisted: number;
  seatsLeft: number;
  myBookingId: string | null;
  myStatus: BookingStatus | null;
}

export interface Booking {
  bookingId: string;
  eventId: string;
  eventTitle: string;
  startsAt: string;
  learnerId: string;
  status: BookingStatus;
  queuePosition: number | null;
  createdAt: string;
}

export interface RosterEntry {
  bookingId: string;
  learnerId: string;
  displayName: string;
  email: string;
  status: BookingStatus;
  queuePosition: number | null;
  createdAt: string;
}

export interface CancelResult {
  cancelledBookingId: string;
  promotedBookingId: string | null;
}

export interface CreateEventRequest {
  title: string;
  labTemplate: string;
  startsAt: string;
  endsAt: string;
  capacity: number;
}

/** RFC 7807 problem details as returned by the API. */
export interface Problem {
  status: number;
  title: string;
  detail?: string;
  code?: number;
  errors?: Record<string, string[]>;
}
