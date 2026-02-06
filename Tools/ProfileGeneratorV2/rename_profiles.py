"""
Script to rename profile files by removing 'Leveling_Guides__' prefix
and update internal Name and LoadProfile references
"""
import os
import re

output_dir = 'output_v2'

def get_new_filename(old_name):
    """Remove Leveling_Guides__ prefix from filename"""
    if old_name.startswith('Leveling_Guides__'):
        return old_name[17:]  # len('Leveling_Guides__') = 17
    return old_name

def update_xml_content(content, old_name, new_name):
    """Update Name and LoadProfile references in XML content"""
    # Update <Name> tag - remove Leveling Guides\\ prefix
    # Pattern: Leveling Guides\\Category\\Zone
    content = re.sub(
        r'(<Name>)Leveling Guides\\\\',
        r'\1',
        content
    )
    
    # Update LoadProfile references
    content = re.sub(
        r'(ProfileName=")Leveling_Guides__',
        r'\1',
        content
    )
    
    return content

def main():
    renamed_count = 0
    updated_content_count = 0
    
    # First pass: collect all renames needed
    renames = []
    
    for root, dirs, files in os.walk(output_dir):
        for filename in files:
            if filename.endswith('.xml'):
                old_path = os.path.join(root, filename)
                
                # Read content
                with open(old_path, 'r', encoding='utf-8') as f:
                    content = f.read()
                
                # Update content (Name and LoadProfile references)
                new_content = update_xml_content(content, filename, get_new_filename(filename))
                
                if new_content != content:
                    with open(old_path, 'w', encoding='utf-8') as f:
                        f.write(new_content)
                    updated_content_count += 1
                
                # Check if needs rename
                if filename.startswith('Leveling_Guides__'):
                    new_filename = get_new_filename(filename)
                    new_path = os.path.join(root, new_filename)
                    renames.append((old_path, new_path))
    
    # Second pass: rename files
    for old_path, new_path in renames:
        if os.path.exists(old_path):
            os.rename(old_path, new_path)
            renamed_count += 1
            print(f"Renamed: {os.path.basename(old_path)} -> {os.path.basename(new_path)}")
    
    print(f"\n=== Summary ===")
    print(f"Files renamed: {renamed_count}")
    print(f"Content updated: {updated_content_count}")

if __name__ == '__main__':
    main()
