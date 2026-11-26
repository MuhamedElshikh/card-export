/** Contracts mirroring the API DTOs (camelCase JSON). */

export interface ScanResult {
  nationalId: string | null;
  fullName: string | null;
  address: string | null;
  occupation: string | null;
  maritalStatus: string | null;
  expiryDate: string | null;
  nationalIdValid: boolean;
  lowConfidence: boolean;
  birthDate: string | null;
  isMale: boolean | null;
  governorateCode: string | null;
  governorateEnglish: string | null;
  governorateArabic: string | null;
}

export interface SaveIdCardRequest {
  nationalId: string;
  fullName: string;
  address: string;
  lowConfidence: boolean;
  occupation?: string | null;
  maritalStatus?: string | null;
  expiryDate?: string | null;
  imageBase64?: string | null;
}

export interface IdCard {
  id: number;
  nationalId: string;
  fullName: string;
  address: string;
  occupation: string | null;
  maritalStatus: string | null;
  expiryDate: string | null;
  birthDate: string;
  isMale: boolean;
  governorateCode: string;
  governorateEnglish: string;
  governorateArabic: string;
  lowConfidence: boolean;
  createdAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ApiProblem {
  code?: string;
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
