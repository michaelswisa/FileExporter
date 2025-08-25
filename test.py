# --- START OF SCRIPT: create_load_test_files.py (v2 - Detailed Report) ---

import os
import math
import time

# Check for tqdm library and provide instructions if missing
try:
    from tqdm import tqdm
except ImportError:
    print("Required library 'tqdm' not found.")
    print("Please install it by running: pip install tqdm")
    exit()

# ===============================================================
# Configuration Variables - You can change these values
# ===============================================================
NUM_FAILED_DIRS = 500000
NUM_NORMAL_DIRS = 50000
NUM_GROUPS = 10
# ===============================================================

def create_files(base_path, num_failed, num_normal, num_groups):
    """
    Creates a directory structure for load testing and provides a detailed report.
    """
    print(f"Starting file creation in: {os.path.abspath(base_path)}")
    print("--------------------------------------------------")
    print(f"Configuration:")
    print(f" - Target Failed Directories: {num_failed:,}")
    print(f" - Target Normal Directories: {num_normal:,}")
    print(f" - Total Target Directories: {num_failed + num_normal:,}")
    print(f" - Number of Groups: {num_groups}")
    print("--------------------------------------------------")

    if not os.path.exists(base_path):
        print(f"Base path '{base_path}' does not exist. Please create it first.")
        return

    # Data structure to hold detailed results for each group
    group_report_data = []

    total_dirs_to_create = num_failed + num_normal
    failed_per_group_calc = math.ceil(num_failed / num_groups)
    normal_per_group_calc = math.ceil(num_normal / num_groups)
    
    start_time = time.time()
    
    failed_dirs_total_created = 0
    normal_dirs_total_created = 0

    with tqdm(total=total_dirs_to_create, unit="dir", desc="Creating Directories") as pbar:
        for i in range(num_groups):
            group_name = f"Group_{i+1:03d}"
            group_path = os.path.join(base_path, group_name)
            os.makedirs(group_path, exist_ok=True)
            
            failed_in_this_group = 0
            normal_in_this_group = 0

            # Create failed directories for this group
            for j in range(failed_per_group_calc):
                if failed_dirs_total_created >= num_failed:
                    break
                
                dir_name = f"Failure_Dir_{failed_dirs_total_created + 1:07d}"
                dir_path = os.path.join(group_path, dir_name)
                os.makedirs(dir_path, exist_ok=True)
                
                fail_file_path = os.path.join(dir_path, "reason.fail")
                with open(fail_file_path, "w") as f:
                    f.write(f"This is a failure reason for directory {dir_name}")
                
                failed_in_this_group += 1
                failed_dirs_total_created += 1
                pbar.update(1)

            # Create normal directories for this group
            for k in range(normal_per_group_calc):
                if normal_dirs_total_created >= num_normal:
                    break

                dir_name = f"Normal_Dir_{normal_dirs_total_created + 1:07d}"
                dir_path = os.path.join(group_path, dir_name)
                os.makedirs(dir_path, exist_ok=True)

                normal_file_path = os.path.join(dir_path, "data.txt")
                with open(normal_file_path, "w") as f:
                    f.write(f"This is normal data for directory {dir_name}")

                normal_in_this_group += 1
                normal_dirs_total_created += 1
                pbar.update(1)
            
            # Store results for this group
            group_report_data.append({
                "name": group_name,
                "path": group_path,
                "failed_count": failed_in_this_group,
                "normal_count": normal_in_this_group,
                "total_in_group": failed_in_this_group + normal_in_this_group
            })
                
    end_time = time.time()
    duration = end_time - start_time

    print("\n\n==================================================")
    print(" SCRIPT FINISHED - LOAD TEST ENVIRONMENT CREATED")
    print("==================================================")
    print(f"Total Time Taken: {duration:.2f} seconds\n")

    print("------------------- DETAILED REPORT --------------------")
    # Print table header
    print(f"{'Group Name':<15} | {'Failed Dirs':>12} | {'Normal Dirs':>12} | {'Total in Group':>15}")
    print("-" * 60)
    
    # Print data for each group
    for group_data in group_report_data:
        print(f"{group_data['name']:<15} | {group_data['failed_count']:>12,} | {group_data['normal_count']:>12,} | {group_data['total_in_group']:>15,}")
    
    print("-" * 60)
    
    # Print totals
    total_failed = sum(g['failed_count'] for g in group_report_data)
    total_normal = sum(g['normal_count'] for g in group_report_data)
    total_dirs = total_failed + total_normal
    print(f"{'TOTALS':<15} | {total_failed:>12,} | {total_normal:>12,} | {total_dirs:>15,}\n")

    print("--------------------- GROUND TRUTH ---------------------")
    print("Use these values to verify the scan results from your server:")
    print(f" > Expected Total Failures: {total_failed:,}")
    for group_data in group_report_data:
        print(f"   - Expected Failures in '{group_data['name']}': {group_data['failed_count']:,}")

    print("\n==================================================")
    print("You can now run your server scan on this directory.")

if __name__ == "__main__":
    current_directory = os.getcwd()
    create_files(current_directory, NUM_FAILED_DIRS, NUM_NORMAL_DIRS, NUM_GROUPS)
    
    
    
    
    
    
    
   # אופציה ב
    
    
  #  pip install tqdm
    
    
    # --- START OF SCRIPT: create_load_test_files.py (v2 - No External Libraries) ---

import os
import math
import time

# ===============================================================
# Configuration Variables - You can change these values
# ===============================================================
NUM_FAILED_DIRS = 500000
NUM_NORMAL_DIRS = 50000
NUM_GROUPS = 10
# ===============================================================

def create_files(base_path, num_failed, num_normal, num_groups):
    """
    Creates a directory structure for load testing and provides a detailed report.
    """
    print(f"Starting file creation in: {os.path.abspath(base_path)}")
    print("--------------------------------------------------")
    print(f"Configuration:")
    print(f" - Target Failed Directories: {num_failed:,}")
    print(f" - Target Normal Directories: {num_normal:,}")
    print(f" - Total Target Directories: {num_failed + num_normal:,}")
    print(f" - Number of Groups: {num_groups}")
    print("--------------------------------------------------")
    print("This may take several minutes. Please be patient...")


    if not os.path.exists(base_path):
        print(f"Base path '{base_path}' does not exist. Please create it first.")
        return

    group_report_data = []
    failed_per_group_calc = math.ceil(num_failed / num_groups)
    normal_per_group_calc = math.ceil(num_normal / num_groups)
    
    start_time = time.time()
    
    failed_dirs_total_created = 0
    normal_dirs_total_created = 0

    for i in range(num_groups):
        group_name = f"Group_{i+1:03d}"
        group_path = os.path.join(base_path, group_name)
        os.makedirs(group_path, exist_ok=True)
        print(f"Creating content for {group_name}...")

        failed_in_this_group = 0
        normal_in_this_group = 0

        # Create failed directories for this group
        for j in range(failed_per_group_calc):
            if failed_dirs_total_created >= num_failed:
                break
            
            dir_name = f"Failure_Dir_{failed_dirs_total_created + 1:07d}"
            dir_path = os.path.join(group_path, dir_name)
            os.makedirs(dir_path, exist_ok=True)
            
            fail_file_path = os.path.join(dir_path, "reason.fail")
            with open(fail_file_path, "w") as f:
                f.write(f"This is a failure reason for directory {dir_name}")
            
            failed_in_this_group += 1
            failed_dirs_total_created += 1

        # Create normal directories for this group
        for k in range(normal_per_group_calc):
            if normal_dirs_total_created >= num_normal:
                break

            dir_name = f"Normal_Dir_{normal_dirs_total_created + 1:07d}"
            dir_path = os.path.join(group_path, dir_name)
            os.makedirs(dir_path, exist_ok=True)

            normal_file_path = os.path.join(dir_path, "data.txt")
            with open(normal_file_path, "w") as f:
                f.write(f"This is normal data for directory {dir_name}")

            normal_in_this_group += 1
            normal_dirs_total_created += 1
        
        group_report_data.append({
            "name": group_name,
            "path": group_path,
            "failed_count": failed_in_this_group,
            "normal_count": normal_in_this_group,
            "total_in_group": failed_in_this_group + normal_in_this_group
        })
            
    end_time = time.time()
    duration = end_time - start_time

    print("\n\n==================================================")
    print(" SCRIPT FINISHED - LOAD TEST ENVIRONMENT CREATED")
    print("==================================================")
    print(f"Total Time Taken: {duration:.2f} seconds\n")

    print("------------------- DETAILED REPORT --------------------")
    print(f"{'Group Name':<15} | {'Failed Dirs':>12} | {'Normal Dirs':>12} | {'Total in Group':>15}")
    print("-" * 60)
    
    for group_data in group_report_data:
        print(f"{group_data['name']:<15} | {group_data['failed_count']:>12,} | {group_data['normal_count']:>12,} | {group_data['total_in_group']:>15,}")
    
    print("-" * 60)
    
    total_failed = sum(g['failed_count'] for g in group_report_data)
    total_normal = sum(g['normal_count'] for g in group_report_data)
    total_dirs = total_failed + total_normal
    print(f"{'TOTALS':<15} | {total_failed:>12,} | {total_normal:>12,} | {total_dirs:>15,}\n")

    print("--------------------- GROUND TRUTH ---------------------")
    print("Use these values to verify the scan results from your server:")
    print(f" > Expected Total Failures: {total_failed:,}")
    for group_data in group_report_data:
        print(f"   - Expected Failures in '{group_data['name']}': {group_data['failed_count']:,}")

    print("\n==================================================")
    print("You can now run your server scan on this directory.")

if __name__ == "__main__":
    current_directory = os.getcwd()
    create_files(current_directory, NUM_FAILED_DIRS, NUM_NORMAL_DIRS, NUM_GROUPS)