import { Nav } from "@/components/Nav";
import { Hero } from "@/components/Hero";
import { FeatureLogFeed } from "@/components/FeatureLogFeed";
import { ComparisonStrip } from "@/components/ComparisonStrip";
import { Quickstart } from "@/components/Quickstart";
import { Footer } from "@/components/Footer";

export default function Home() {
  return (
    <div className="flex min-h-full flex-1 flex-col">
      <Nav />
      <main className="flex-1">
        <Hero />
        <FeatureLogFeed />
        <ComparisonStrip />
        <Quickstart />
      </main>
      <Footer />
    </div>
  );
}
