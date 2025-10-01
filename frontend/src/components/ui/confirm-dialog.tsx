import { FC } from 'react';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { ConfirmDialogProps } from '@/lib/types';

const ConfirmDialog: FC<ConfirmDialogProps> = ({
  isOpen,
  title,
  message,
  confirmText = 'DELETE',
  cancelText = 'CANCEL',
  onConfirm,
  onCancel,
}) => {
  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && onCancel()}>
      <AlertDialogContent className="bg-card border border-primary">
        <AlertDialogHeader>
          <AlertDialogTitle className="text-xl text-primary font-bold">
            {title}
          </AlertDialogTitle>
          <AlertDialogDescription className="text-foreground">
            {message}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter className="mt-4 flex space-x-3">
          <AlertDialogCancel 
            onClick={onCancel}
            className="bg-card border border-secondary text-primary px-4 py-2 rounded hover:bg-black transition-colors"
          >
            {cancelText}
          </AlertDialogCancel>
          <AlertDialogAction
            onClick={onConfirm}
            className="bg-destructive text-white px-4 py-2 rounded hover:bg-red-600 transition-colors"
          >
            {confirmText}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
};

export default ConfirmDialog;
