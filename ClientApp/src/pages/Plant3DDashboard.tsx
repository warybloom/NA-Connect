import DashboardHeader from "@/components/plant3d/DashboardHeader";
import MainTabs from "@/components/plant3d/MainTabs";

const Plant3DDashboard = () => {
  return (
    <div className="min-h-screen bg-background">
      <DashboardHeader />
      <div className="py-6">
        <MainTabs />
      </div>
    </div>
  );
};

export default Plant3DDashboard;