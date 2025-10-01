import { type ClassValue, clsx } from "clsx";
import { twMerge } from "tailwind-merge";
import { 
  File,
  FileText,
  FileSpreadsheet,
  FileCode,
  Image as ImageIcon,
  FileType
} from 'lucide-react';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 Bytes";
  const k = 1024;
  const sizes = ["Bytes", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i];
}

export function truncateString(str: string, length: number): string {
  if (str.length <= length) return str;
  return str.slice(0, length) + "...";
}

export const generateRandomId = (): string => {
  return Math.random().toString(36).substring(2, 15);
};

// export function getFileIconByExtension(fileName: string): string {
//   const extension = fileName.split('.').pop()?.toLowerCase() || '';
  
//   switch (extension) {
//     case 'pdf':
//       return 'file-pdf';
//     case 'doc':
//     case 'docx':
//       return 'file-text';
//     case 'xls':
//     case 'xlsx':
//     case 'csv':
//       return 'file-spreadsheet';
//     case 'jpg':
//     case 'jpeg':
//     case 'png':
//     case 'gif':
//     case 'bmp':
//       return 'image';
//     case 'json':
//     case 'xml':
//       return 'file-code';
//     case 'txt':
//       return 'file-text';
//     default:
//       return 'file';
//   }
// }

export function getFileIconByExtension(fileName: string) {
  const extension = fileName.split('.').pop()?.toLowerCase() || '';
  
  switch (extension) {
    case 'pdf':
      return <FileType size={16} />; // Using FileType instead of FilePdf
    case 'doc':
    case 'docx':
      return <FileText size={16} />;
    case 'xls':
    case 'xlsx':
    case 'csv':
      return <FileSpreadsheet size={16} />;
    case 'jpg':
    case 'jpeg':
    case 'png':
    case 'gif':
    case 'bmp':
      return <ImageIcon size={16} />;
    case 'json':
    case 'xml':
      return <FileCode size={16} />;
    case 'txt':
      return <FileText size={16} />;
    default:
      return <File size={16} />;
  }
}
