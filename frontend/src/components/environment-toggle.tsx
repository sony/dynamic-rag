import { FC } from 'react';
import { Switch } from '@/components/ui/switch';
import { Label } from '@/components/ui/label';
import { useEnvironment } from '@/hooks/use-environment';

const EnvironmentToggle: FC = () => {
  const { isDevelopmentMode, setDevelopmentMode } = useEnvironment();

  const handleToggle = (checked: boolean) => {
    setDevelopmentMode(!checked);
  };

  return (
    <div className="fixed top-4 right-4 flex items-center z-50 bg-card rounded-md p-2">
      <span className={`text-sm mr-2 ${isDevelopmentMode ? 'text-accent' : 'text-muted-foreground'}`}>DEV</span>
      <Switch 
        checked={!isDevelopmentMode} 
        onCheckedChange={handleToggle}
        className="mx-2"
      />
      <span className={`text-sm ${!isDevelopmentMode ? 'text-accent' : 'text-muted-foreground'}`}>PROD</span>
    </div>
  );
};

export default EnvironmentToggle;
