// file-list.tsx
import { FC } from 'react';
import FileItem, { FileInfo } from './file-item';

interface FileListProps {
  files: FileInfo[];
  collectionName: string;
  onRemoveFile: (collectionName: string, filePath: string) => void;
}

const FileList: FC<FileListProps> = ({ files, collectionName, onRemoveFile }) => {
  if (!files || files.length === 0) {
    return (
      <div className="text-muted-foreground text-sm italic">
        No files in this collection
      </div>
      
    );
  }

  return (
    <div className="space-y-1">
      {files.map((file, index) => (
        <FileItem
          key={`${file.filename}-${index}`}
          file={file}
          collectionName={collectionName}
          onRemove={onRemoveFile}
        />
      ))}
    </div>
  );
};

export default FileList;
